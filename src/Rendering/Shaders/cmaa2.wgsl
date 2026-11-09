// Conservative Morphological Anti-Aliasing 2 (CMAA2), ported to WGSL from Intel's CMAA2.hlsl, version 2.3.
//
// Copyright (c) 2018, Intel Corporation. Author: Filip Strugar. https://github.com/GameTechDev/CMAA2
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with
// the License. You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0. Unless required by
// applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language
// governing permissions and limitations under the License.
//
// Changes from the original: WGSL; the defaults kept (HIGH quality preset, luma edge detection computed in place, no
// extra sharpness, no MSAA, edges packed two pixels to a texel, blend items collected and expanded in workgroup
// memory); loads clamped to the image, where the original read zero past its edges; the list heads are a buffer (WGSL
// has no atomics on textures); the edges are an r32uint texture; and the blended pixels go to an overlay texture in
// linear light (rgba16float, alpha one where written), not back into the colour, which the frame then shows over it.
//
// The passes, in one compute pass (ViewportRenderer.Antialiasing.cs): cs_edges finds the edges of the frame's 2x2
// quads and lists the pixels where a shape may start; cs_args_candidates sizes the next dispatch; cs_process_candidates
// works out each candidate's simple shape blend and traces Z shapes along its edges, storing blended colours per quad;
// cs_args_apply sizes the last dispatch; cs_apply averages each pixel's blended colours into the overlay.

const INPUT_KERNEL: u32 = 16u;
const OUTPUT_KERNEL: i32 = 14;
const PROCESS_CANDIDATES_THREADS: u32 = 128u;
const DEFERRED_APPLY_THREADS: u32 = 32u;
const BLEND_ITEM_SLM_SIZE: u32 = 768u;
const MAX_LINE_LENGTH: f32 = 86.0;
const EDGE_THRESHOLD: f32 = 0.07;
const LOCAL_CONTRAST_ADAPTATION: f32 = 0.10;
const SIMPLE_SHAPE_BLURINESS: f32 = 0.10;
const SYMMETRY_CORRECTION_OFFSET: f32 = 0.22;
const DAMPENING_EFFECT: f32 = 0.15;
const NO_ITEM: u32 = 0xFFFFFFFFu;

// The control buffer's slots: the count the current indirect dispatch works through, then the three counters.
const CONTROL_COUNT: u32 = 3u;
const CONTROL_CANDIDATES: u32 = 4u;
const CONTROL_LOCATIONS: u32 = 8u;
const CONTROL_ITEMS: u32 = 12u;

// The frame, through its sRGB view: linear colour.
@group(0) @binding(0) var color_in: texture_2d<f32>;
@group(0) @binding(1) var edges_out: texture_storage_2d<r32uint, write>;
@group(0) @binding(2) var edges_in: texture_2d<u32>;
@group(0) @binding(3) var<storage, read_write> candidates: array<u32>;
@group(0) @binding(4) var<storage, read_write> heads: array<atomic<u32>>;
@group(0) @binding(5) var<storage, read_write> control: array<atomic<u32>>;
@group(0) @binding(6) var<storage, read_write> items: array<vec2u>;
@group(0) @binding(7) var<storage, read_write> locations: array<u32>;
@group(0) @binding(8) var<storage, read_write> indirect: array<u32>;
@group(0) @binding(9) var color_out: texture_storage_2d<rgba16float, write>;

fn image_size() -> vec2i {
    return vec2i(textureDimensions(color_in));
}

// A pixel's colour, the nearest one in the image for one past its edges.
fn load_color(position: vec2i) -> vec3f {
    let size = image_size();
    return textureLoad(color_in, clamp(position, vec2i(0), size - 1), 0).rgb;
}

// How .rgba (bits 1, 2, 4, 8) map to a pixel's edges: right, bottom, left, top.
fn pack_edges(edges: vec4f) -> u32 {
    return u32(dot(edges, vec4f(1.0, 2.0, 4.0, 8.0)));
}

fn unpack_edges(value: u32) -> vec4f {
    return vec4f(f32((value & 1u) != 0u), f32((value & 2u) != 0u), f32((value & 4u) != 0u), f32((value & 8u) != 0u));
}

// A pixel's four edges, two pixels to a texel; none past the image.
fn load_edge(position: vec2i) -> u32 {
    let size = image_size();
    if (any(position < vec2i(0)) || any(position >= size)) {
        return 0u;
    }
    let packed = textureLoad(edges_in, vec2i(position.x / 2, position.y), 0).x;
    return (packed >> (u32(position.x % 2) * 4u)) & 0xFu;
}

fn f32_to_f16(value: f32) -> u32 {
    return pack2x16float(vec2f(value, 0.0)) & 0xFFFFu;
}

fn f16_to_f32(value: u32) -> f32 {
    return unpack2x16float(value).x;
}

// R11G11B10 with a four-bit exponent (MiniEngine's PixelPacking_R11G11B10, MIT): enough for 8-bit sRGB in [0, 2).
fn pack_color(rgb: vec3f) -> u32 {
    let c = clamp(rgb, vec3f(0.0), vec3f(bitcast<f32>(0x3FFFFFFFu)));
    let r = ((f32_to_f16(c.r) + 4u) >> 3u) & 0x000007FFu;
    let g = ((f32_to_f16(c.g) + 4u) << 8u) & 0x003FF800u;
    let b = ((f32_to_f16(c.b) + 8u) << 18u) & 0xFFC00000u;
    return r | g | b;
}

fn unpack_color(rgb: u32) -> vec3f {
    return vec3f(f16_to_f32((rgb << 3u) & 0x3FF8u), f16_to_f32((rgb >> 8u) & 0x3FF8u), f16_to_f32((rgb >> 18u) & 0x3FF0u));
}

// FXAA's and CMAA2's luma for edges: Rec. 601 weights on roughly gamma-compressed colour.
fn luma_for_edges(linear_rgb: vec3f) -> f32 {
    return dot(sqrt(max(linear_rgb, vec3f(0.0))), vec3f(0.299, 0.587, 0.114));
}

// A blended colour for a pixel, kept on its quad's list for the apply pass.
fn store_color_sample(pixel: vec2i, color: vec3f, complex_shape: bool) {
    let size = image_size();
    if (any(pixel < vec2i(0)) || any(pixel >= size)) {
        return;
    }
    let counter = atomicAdd(&control[CONTROL_ITEMS], 1u);
    if (counter >= arrayLength(&items) || counter >= (1u << 26u)) {
        return;
    }
    let quad = vec2u(pixel) / 2u;
    let offset_xy = (u32(pixel.y) % 2u) * 2u + (u32(pixel.x) % 2u);
    // {2 bits for the 2x2 quad location}, {3 bits for the MSAA sample, always 0}, {1 bit for complex shapes}, {26 bits of index}
    let header = (offset_xy << 30u) | (u32(complex_shape) << 26u);
    let quads_x = (u32(size.x) + 1u) / 2u;
    let original = atomicExchange(&heads[quad.y * quads_x + quad.x], counter | header);
    items[counter] = vec2u(original, pack_color(color));
    // The first on its quad: the quad goes on the list of those the apply pass blends.
    if (original == NO_ITEM) {
        let location = atomicAdd(&control[CONTROL_LOCATIONS], 1u);
        if (location < arrayLength(&locations)) {
            locations[location] = (quad.x << 16u) | quad.y;
        }
    }
}

var<workgroup> frac_edges_h: array<vec4f, 256>;
var<workgroup> frac_edges_v: array<vec4f, 256>;

// The quad at addr's horizontal (in y) and vertical (in x) edge strengths: top left, top right, bottom left, bottom right.
fn quad_hv(addr: u32) -> array<vec2f, 4> {
    let h = frac_edges_h[addr];
    let v = frac_edges_v[addr];
    return array<vec2f, 4>(vec2f(v.x, h.x), vec2f(v.y, h.y), vec2f(v.z, h.z), vec2f(v.w, h.w));
}

fn local_contrast_v(x: i32, y: i32, n: ptr<function, array<array<vec2f, 4>, 4>>) -> f32 {
    return max(max((*n)[x + 1][y + 0].y, (*n)[x + 1][y + 1].y), max((*n)[x + 2][y + 0].y, (*n)[x + 2][y + 1].y)) * LOCAL_CONTRAST_ADAPTATION;
}

fn local_contrast_h(x: i32, y: i32, n: ptr<function, array<array<vec2f, 4>, 4>>) -> f32 {
    return max(max((*n)[x + 0][y + 1].x, (*n)[x + 1][y + 1].x), max((*n)[x + 0][y + 2].x, (*n)[x + 1][y + 2].x)) * LOCAL_CONTRAST_ADAPTATION;
}

@compute @workgroup_size(16, 16, 1)
fn cs_edges(@builtin(workgroup_id) group: vec3u, @builtin(local_invocation_id) thread: vec3u) {
    // The quad's position in the input kernel, which reaches one quad past the output kernel all round.
    let pixel = (vec2i(group.xy) * OUTPUT_KERNEL + vec2i(thread.xy) - 1) * 2;
    let row = INPUT_KERNEL;
    let center = thread.x + thread.y * row;
    let in_output = !(thread.x == INPUT_KERNEL - 1u || thread.x == 0u || thread.y == INPUT_KERNEL - 1u || thread.y == 0u);

    var lumas: array<f32, 8>;
    for (var i = 0; i < 8; i++) {
        lumas[i] = luma_for_edges(load_color(pixel + vec2i(i % 3, i / 3)));
    }
    let qe0 = vec2f(abs(lumas[0] - lumas[1]), abs(lumas[0] - lumas[3]));
    let qe1 = vec2f(abs(lumas[1] - lumas[2]), abs(lumas[1] - lumas[4]));
    let qe2 = vec2f(abs(lumas[3] - lumas[4]), abs(lumas[3] - lumas[6]));
    let qe3 = vec2f(abs(lumas[4] - lumas[5]), abs(lumas[4] - lumas[7]));
    frac_edges_v[center] = vec4f(qe0.x, qe1.x, qe2.x, qe3.x);
    frac_edges_h[center] = vec4f(qe0.y, qe1.y, qe2.y, qe3.y);

    workgroupBarrier();

    var out_edges = vec4u(0u);
    if (in_output) {
        let top_row = frac_edges_h[center - row].zw;
        let left_column = frac_edges_v[center - 1u].yw;
        let some_edges = any((vec4f(qe0, qe1) + vec4f(qe2, qe3) + vec4f(top_row.x, top_row.y, left_column.x, left_column.y)) != vec4f(0.0));
        if (some_edges) {
            let size = image_size();
            let quads_x = (u32(size.x) + 1u) / 2u;
            let quad = vec2u(pixel) / 2u;
            if (quad.x < quads_x && quad.y < (u32(size.y) + 1u) / 2u) {
                atomicStore(&heads[quad.y * quads_x + quad.x], NO_ITEM);
            }

            // Local contrast adaptation over the neighbouring quads' edges.
            var n: array<array<vec2f, 4>, 4>;
            let q0 = quad_hv(center - row - 1u);
            n[0][0] = q0[3];
            let q1 = quad_hv(center - row);
            n[1][0] = q1[2];
            n[2][0] = q1[3];
            let q2 = quad_hv(center - row + 1u);
            n[3][0] = q2[2];
            let q3 = quad_hv(center - 1u);
            n[0][1] = q3[1];
            n[0][2] = q3[3];
            let q4 = quad_hv(center + 1u);
            n[3][1] = q4[0];
            n[3][2] = q4[2];
            let q5 = quad_hv(center - 1u + row);
            n[0][3] = q5[1];
            let q6 = quad_hv(center + row);
            n[1][3] = q6[0];
            n[2][3] = q6[1];
            n[1][0].y = top_row.x;
            n[2][0].y = top_row.y;
            n[0][1].x = left_column.x;
            n[0][2].x = left_column.y;
            n[1][1] = qe0;
            n[2][1] = qe1;
            n[1][2] = qe2;
            n[2][2] = qe3;

            let t = EDGE_THRESHOLD;
            let top0 = f32((top_row.x - local_contrast_h(0, -1, &n)) > t);
            let top1 = f32((top_row.y - local_contrast_h(1, -1, &n)) > t);
            let left0 = f32((left_column.x - local_contrast_v(-1, 0, &n)) > t);
            let left1 = f32((left_column.y - local_contrast_v(-1, 1, &n)) > t);

            var ce: array<vec4f, 4>;
            ce[0].x = f32((qe0.x - local_contrast_v(0, 0, &n)) > t);
            ce[0].y = f32((qe0.y - local_contrast_h(0, 0, &n)) > t);
            ce[1].x = f32((qe1.x - local_contrast_v(1, 0, &n)) > t);
            ce[1].y = f32((qe1.y - local_contrast_h(1, 0, &n)) > t);
            ce[2].x = f32((qe2.x - local_contrast_v(0, 1, &n)) > t);
            ce[2].y = f32((qe2.y - local_contrast_h(0, 1, &n)) > t);
            ce[3].x = f32((qe3.x - local_contrast_v(1, 1, &n)) > t);
            ce[3].y = f32((qe3.y - local_contrast_h(1, 1, &n)) > t);

            ce[0].z = left0;
            ce[1].z = ce[0].x;
            ce[2].z = left1;
            ce[3].z = ce[2].x;
            ce[0].w = top0;
            ce[1].w = top1;
            ce[2].w = ce[0].y;
            ce[3].w = ce[1].y;

            let offsets = array<vec2i, 4>(vec2i(0, 0), vec2i(1, 0), vec2i(0, 1), vec2i(1, 1));
            for (var i = 0; i < 4; i++) {
                let local = pixel + offsets[i];
                let e = ce[i];
                // Two edges meeting at a corner: where a shape may start.
                let candidate = (e.x * e.y + e.y * e.z + e.z * e.w + e.w * e.x) != 0.0;
                if (candidate && all(local < size)) {
                    let index = atomicAdd(&control[CONTROL_CANDIDATES], 1u);
                    if (index < arrayLength(&candidates)) {
                        candidates[index] = (u32(local.x) << 18u) | u32(local.y);
                    }
                }
                out_edges[i] = pack_edges(e);
            }
        }
    }

    if (in_output) {
        let texel = vec2i(pixel.x / 2, pixel.y);
        let edges_size = vec2i(textureDimensions(edges_out));
        if (texel.x < edges_size.x && texel.y < edges_size.y) {
            textureStore(edges_out, texel, vec4u((out_edges[1] << 4u) | out_edges[0], 0u, 0u, 0u));
        }
        if (texel.x < edges_size.x && texel.y + 1 < edges_size.y) {
            textureStore(edges_out, texel + vec2i(0, 1), vec4u((out_edges[3] << 4u) | out_edges[2], 0u, 0u, 0u));
        }
    }
}

// The process pass's dispatch, from the candidates found.
@compute @workgroup_size(1, 1, 1)
fn cs_args_candidates() {
    let count = min(atomicLoad(&control[CONTROL_CANDIDATES]), arrayLength(&candidates));
    indirect[0] = min((count + PROCESS_CANDIDATES_THREADS - 1u) / PROCESS_CANDIDATES_THREADS, 65535u);
    indirect[1] = 1u;
    indirect[2] = 1u;
    atomicStore(&control[CONTROL_COUNT], count);
}

// The apply pass's dispatch, from the quads blended; and the counters back to zero for the next frame.
@compute @workgroup_size(1, 1, 1)
fn cs_args_apply() {
    let count = min(atomicLoad(&control[CONTROL_LOCATIONS]), arrayLength(&locations));
    indirect[0] = 1u;
    indirect[1] = min((count + DEFERRED_APPLY_THREADS - 1u) / DEFERRED_APPLY_THREADS, 65535u);
    indirect[2] = 1u;
    atomicStore(&control[CONTROL_COUNT], count);
    atomicStore(&control[CONTROL_CANDIDATES], 0u);
    atomicStore(&control[CONTROL_LOCATIONS], 0u);
    atomicStore(&control[CONTROL_ITEMS], 0u);
}

// The blend weights of an L-like shape at a pixel, from its edges and its neighbours': from the left, above, right, below.
fn simple_shape_blend(edges: vec4f, left: vec4f, right: vec4f, top: vec4f, bottom: vec4f) -> vec4f {
    var from_right = edges.r;
    var from_below = edges.g;
    var from_left = edges.b;
    var from_above = edges.a;
    var blur = SIMPLE_SHAPE_BLURINESS;
    let edge_count = dot(edges, vec4f(1.0));
    let edges_around = dot(left.bga + right.rga + top.rba + bottom.rgb, vec3f(1.0));
    if (edge_count == 2.0) {
        blur *= 0.75;
        let k = 0.9;
        from_right += k * (edges.g * top.r * (1.0 - left.g) + edges.a * bottom.r * (1.0 - left.a));
        from_below += k * (edges.b * right.g * (1.0 - top.b) + edges.r * left.g * (1.0 - top.r));
        from_left += k * (edges.a * bottom.b * (1.0 - right.a) + edges.g * top.b * (1.0 - right.g));
        from_above += k * (edges.r * left.a * (1.0 - bottom.r) + edges.b * right.a * (1.0 - bottom.b));
    }
    // Less blur among many edges: keeps text and texture detail.
    blur *= saturate(1.30 - edges_around / 10.0);
    return vec4f(from_left, from_above, from_right, from_below) * blur;
}

// How far a Z shape's edges run to the left and right of a pixel, along its line.
fn find_z_line_lengths(position: vec2i, horizontal: bool, inverted: bool, step_right: vec2i) -> vec2f {
    var mask_left: u32;
    var mask_right: u32;
    if (horizontal) {
        mask_left = 0x08u;
        mask_right = 0x02u;
    } else {
        mask_left = 0x04u;
        mask_right = 0x01u;
    }
    if (inverted) {
        let swap = mask_left;
        mask_left = mask_right;
        mask_right = swap;
    }
    var continue_left = true;
    var continue_right = true;
    var length_left = 1.0;
    var length_right = 1.0;
    loop {
        let edge_left = load_edge(position - step_right * i32(length_left));
        let edge_right = load_edge(position + step_right * (i32(length_right) + 1));
        continue_left = continue_left && ((edge_left & mask_left) == mask_left);
        continue_right = continue_right && ((edge_right & mask_right) == mask_right);
        length_left += f32(continue_left);
        length_right += f32(continue_right);
        var longest = max(length_right, length_left);
        if (!continue_left && !continue_right) {
            longest = MAX_LINE_LENGTH;
        }
        if (longest >= min(MAX_LINE_LENGTH, 1.25 * min(length_right, length_left) - 0.25)) {
            break;
        }
    }
    return vec2f(length_left, length_right);
}

var<workgroup> blend_item_count: atomic<u32>;
var<workgroup> blend_items: array<vec2u, 768>;

// A Z shape's blends, for the workgroup to work out together; false where there's no room left for them.
fn collect_blend_zs(position: vec2i, horizontal: bool, inverted: bool, quality: f32, length_left: f32, length_right: f32) -> bool {
    let left_odd = SYMMETRY_CORRECTION_OFFSET * (length_left % 2.0);
    let right_odd = SYMMETRY_CORRECTION_OFFSET * (length_right % 2.0);
    let dampen = saturate((length_left + length_right - quality) * DAMPENING_EFFECT);
    let loop_from = -floor((length_left + 1.0) / 2.0) + 1.0;
    let loop_to = floor((length_right + 1.0) / 2.0);
    let count = u32(loop_to - loop_from + 1.0);
    var index = atomicAdd(&blend_item_count, count);
    if (index + count > BLEND_ITEM_SLM_SIZE) {
        return false;
    }
    let total_length = (loop_to - loop_from) + 1.0 - left_odd - right_odd;
    let lerp_step = 1.0 / total_length;
    let lerp_from_k = (0.5 - left_odd - loop_from) * lerp_step;
    let header = (u32(position.x) << 18u) | u32(position.y);
    let value_static = (u32(horizontal) << 31u) | (u32(inverted) << 30u);
    for (var i = loop_from; i <= loop_to; i += 1.0) {
        let second_part = f32(i > 0.0);
        let source_offset = 1.0 - second_part * 2.0;
        var lerp_k = (lerp_step * i + lerp_from_k) * source_offset + second_part;
        lerp_k *= dampen;
        let encoded = value_static | (u32(i + 256.0) << 20u) | (u32(source_offset + 256.0) << 10u) | u32(saturate(lerp_k) * 1023.0 + 0.5);
        blend_items[index] = vec2u(header, encoded);
        index++;
    }
    return true;
}

fn blend_z_pixel(position: vec2i, horizontal: bool, inverted: bool, step_index: f32, source_offset: f32, lerp_k: f32) {
    let step_right = select(vec2i(0, -1), vec2i(1, 0), horizontal);
    var blend_dir = select(vec2i(-1, 0), vec2i(0, -1), horizontal);
    if (inverted) {
        blend_dir = -blend_dir;
    }
    let pixel = position + step_right * i32(step_index);
    let center = load_color(pixel);
    let source = load_color(pixel + blend_dir * i32(source_offset));
    store_color_sample(pixel, mix(center, source, lerp_k), true);
}

// The same blends, worked out by this thread alone, when the workgroup has no room for them.
fn blend_zs(position: vec2i, horizontal: bool, inverted: bool, quality: f32, length_left: f32, length_right: f32) {
    let left_odd = SYMMETRY_CORRECTION_OFFSET * (length_left % 2.0);
    let right_odd = SYMMETRY_CORRECTION_OFFSET * (length_right % 2.0);
    let dampen = saturate((length_left + length_right - quality) * DAMPENING_EFFECT);
    let loop_from = -floor((length_left + 1.0) / 2.0) + 1.0;
    let loop_to = floor((length_right + 1.0) / 2.0);
    let total_length = (loop_to - loop_from) + 1.0 - left_odd - right_odd;
    let lerp_step = 1.0 / total_length;
    let lerp_from_k = (0.5 - left_odd - loop_from) * lerp_step;
    for (var i = loop_from; i <= loop_to; i += 1.0) {
        let second_part = f32(i > 0.0);
        let source_offset = 1.0 - second_part * 2.0;
        let lerp_k = ((lerp_step * i + lerp_from_k) * source_offset + second_part) * dampen;
        blend_z_pixel(position, horizontal, inverted, i, source_offset, lerp_k);
    }
}

// How well the edges around a pixel make a horizontal Z shape, inverted (x) or not (y).
fn detect_zs_horizontal(edges: vec4f, m1p0: vec4f, p1p0: vec4f, p2p0: vec4f) -> vec2f {
    var inverted = edges.r * edges.g * p1p0.a;
    inverted *= 2.0 + (m1p0.g + p2p0.a) - (edges.a + p1p0.g) - 0.7 * (p2p0.g + m1p0.a + edges.b + p1p0.r);
    var normal = edges.r * edges.a * p1p0.g;
    normal *= 2.0 + (m1p0.a + p2p0.g) - (edges.g + p1p0.a) - 0.7 * (p2p0.a + m1p0.g + edges.b + p1p0.r);
    return vec2f(inverted, normal);
}

@compute @workgroup_size(128, 1, 1)
fn cs_process_candidates(@builtin(global_invocation_id) id: vec3u, @builtin(local_invocation_id) thread: vec3u) {
    if (thread.x == 0u) {
        atomicStore(&blend_item_count, 0u);
    }
    workgroupBarrier();

    let candidate_count = atomicLoad(&control[CONTROL_COUNT]);
    if (id.x < candidate_count) {
        let pixel_id = candidates[id.x];
        let pixel = vec2i(i32(pixel_id >> 18u), i32(pixel_id & 0x3FFFu));

        let edges = unpack_edges(load_edge(pixel));
        let edges_left = unpack_edges(load_edge(pixel + vec2i(-1, 0)));
        let edges_right = unpack_edges(load_edge(pixel + vec2i(1, 0)));
        let edges_bottom = unpack_edges(load_edge(pixel + vec2i(0, 1)));
        let edges_top = unpack_edges(load_edge(pixel + vec2i(0, -1)));

        // The simple shape blend.
        {
            let blend = simple_shape_blend(edges, edges_left, edges_right, edges_top, edges_bottom);
            let center_weight = 1.0 - dot(blend, vec4f(1.0));
            var color = load_color(pixel) * center_weight;
            if (blend.x > 0.0) {
                color += blend.x * load_color(pixel + vec2i(-1, 0));
            }
            if (blend.y > 0.0) {
                color += blend.y * load_color(pixel + vec2i(0, -1));
            }
            if (blend.z > 0.0) {
                color += blend.z * load_color(pixel + vec2i(1, 0));
            }
            if (blend.w > 0.0) {
                color += blend.w * load_color(pixel + vec2i(0, 1));
            }
            store_color_sample(pixel, color, false);
        }

        // Z shapes, horizontal or vertical.
        {
            var horizontal = true;
            var inverted = false;
            let h = detect_zs_horizontal(edges, edges_left, edges_right, unpack_edges(load_edge(pixel + vec2i(2, 0))));
            var max_score = max(h.x, h.y);
            if (max_score > 0.0) {
                inverted = h.x > h.y;
            }
            let v = detect_zs_horizontal(edges.argb, edges_bottom.argb, edges_top.argb, unpack_edges(load_edge(pixel + vec2i(0, -2))).argb);
            let vertical_score = max(v.x, v.y);
            if (vertical_score > max_score) {
                max_score = vertical_score;
                horizontal = false;
                inverted = v.x > v.y;
            }
            if (max_score > 0.0) {
                // 0: the best; 1: some edges missing but fine; 2 and 3: dubious but better than nothing.
                let quality = floor(clamp(4.0 - max_score, 0.0, 3.0));
                let step_right = select(vec2i(0, -1), vec2i(1, 0), horizontal);
                var lengths = find_z_line_lengths(pixel, horizontal, inverted, step_right);
                lengths -= vec2f(quality);
                if (lengths.x + lengths.y >= 5.0) {
                    if (!collect_blend_zs(pixel, horizontal, inverted, quality, lengths.x, lengths.y)) {
                        blend_zs(pixel, horizontal, inverted, quality, lengths.x, lengths.y);
                    }
                }
            }
        }
    }

    // The Z shapes' blends, spread over the whole workgroup.
    workgroupBarrier();
    let total = min(BLEND_ITEM_SLM_SIZE, atomicLoad(&blend_item_count));
    let loops = (total + (PROCESS_CANDIDATES_THREADS - 1u) - thread.x) / PROCESS_CANDIDATES_THREADS;
    for (var l = 0u; l < loops; l++) {
        let item = blend_items[l * PROCESS_CANDIDATES_THREADS + thread.x];
        let start = vec2i(i32(item.x >> 18u), i32(item.x & 0x3FFFu));
        let horizontal = ((item.y >> 31u) & 1u) != 0u;
        let inverted = ((item.y >> 30u) & 1u) != 0u;
        let step_index = f32((item.y >> 20u) & 0x3FFu) - 256.0;
        let source_offset = f32((item.y >> 10u) & 0x3FFu) - 256.0;
        let lerp_k = f32(item.y & 0x3FFu) / 1023.0;
        blend_z_pixel(start, horizontal, inverted, step_index, source_offset, lerp_k);
    }
}

@compute @workgroup_size(4, 32, 1)
fn cs_apply(@builtin(global_invocation_id) id: vec3u, @builtin(local_invocation_id) thread: vec3u) {
    let count = atomicLoad(&control[CONTROL_COUNT]);
    let current = id.y;
    let quad_offset = thread.x;
    if (current >= count) {
        return;
    }
    let location = locations[current];
    let quad = vec2u(location >> 16u, location & 0xFFFFu);
    let offsets = array<vec2u, 4>(vec2u(0u, 0u), vec2u(1u, 0u), vec2u(0u, 1u), vec2u(1u, 1u));
    let pixel = quad * 2u + offsets[quad_offset];
    let size = textureDimensions(color_out);
    let quads_x = (size.x + 1u) / 2u;
    var link = atomicLoad(&heads[quad.y * quads_x + quad.x]);

    var sum = vec4f(0.0);
    // Bounded, so bad data can't hang the GPU.
    for (var i = 0u; link != NO_ITEM && i < 32u; i++) {
        let offset_xy = (link >> 30u) & 0x03u;
        let complex_shape = ((link >> 26u) & 0x01u) != 0u;
        let index = link & ((1u << 26u) - 1u);
        if (index >= arrayLength(&items)) {
            break;
        }
        let value = items[index];
        link = value.x;
        if (offset_xy == quad_offset) {
            let weight = 0.8 + f32(complex_shape);
            sum += vec4f(unpack_color(value.y) * weight, weight);
        }
    }
    if (sum.a == 0.0 || any(pixel >= size)) {
        return;
    }
    textureStore(color_out, vec2i(pixel), vec4f(sum.rgb / sum.a, 1.0));
}
