// GTA V's trees, as the game's tree shaders draw them (checked against Enhanced's): what their vertex shaders do to a vertex in its model's space, before its placement's
// matrix, for the main view (surface.wgsl) and the sun's shadow (shadow.wgsl, gta_world_shadow.wgsl). Standalone: the
// material's tree settings, the camera's axes and the placement's are passed in.
//   - trees_lod2's billboards (VS_PropFoliageDeferredLod2): each corner of a quad is stored at its middle, with where it
//     is on the quad in the first UV set; the quad is spread across the camera (or upright and turned to it, as TEXCOORD3.x
//     says) by treeLod2Params times its own size (TEXCOORD2), then turned back by the placement's rotation, which its
//     matrix applies again. The cascades' version (VS_Cascade_PropFoliageLod2) spreads it across the sun's view, pushed
//     away from the sun by 0.3 of its width.
//   - The cards turned to the camera (VS_PropFoliageDeferred of the _camera_facing and _camera_aligned shaders): _camera_facing turns the card's model X, Z and Y to the
//     camera's right, up and back; _camera_aligned keeps it upright, turned about the vertical towards the camera. The
//     cascades leave the cards as they are, pushed along the sun's back by ShadowFacingOffset (VS_Cascade_Micromovments).

// The axes of the view a tree is drawn for: right, up and back (towards the viewer), as the game's inverse view rows.
struct TreeView {
    right: vec3f,
    up: vec3f,
    back: vec3f,
};

struct TreeVertex {
    position: vec3f,
    normal: vec3f,
    // The UV set the diffuse map is read on: the billboards' second, the others' first.
    uv: vec2f,
};

// The Material's tree settings (surface.wgsl): program (1 foliage, 2 lod, 3 billboard), alignment (1 facing, 2 aligned),
// AlphaScale, AlphaTest; UseTreeNormals, SelfShadowing, ShadowFalloff; treeLod2Params.xy, ShadowFacingOffset;
// treeLod2Normal.
struct TreeSettings {
    tree: vec4f,
    shade: vec4f,
    size: vec4f,
    lod2_normal: vec4f,
};

fn tree_program(settings: TreeSettings) -> u32 {
    return u32(settings.tree.x + 0.5);
}

// normalize(cross(v, (0, 0, -1))), the game's way across a view kept upright; straight up or down it has none, and the
// view's right stands in.
fn tree_across(v: vec3f, fallback: vec3f) -> vec3f {
    let across = vec3f(-v.y, v.x, 0.0);
    let length2 = dot(across, across);
    return select(fallback, across * inverseSqrt(length2), length2 > 1e-12);
}

// A tree's vertex in its model's space for the main view. tangent and uv2 carry the billboards' size and alignment, uv1
// and uv2.x the cards' centre from the vertex (DrawableScene's VertexReader); axes are the placement's rotation's columns
// (identity in the model viewer).
fn tree_vertex(settings: TreeSettings, position: vec3f, normal: vec3f, tangent: vec4f, uv0: vec2f, uv1: vec2f, uv2: vec2f,
    axes: mat3x3f, view: TreeView) -> TreeVertex {
    var out: TreeVertex;
    out.position = position;
    out.normal = normal;
    out.uv = uv0;
    let program = tree_program(settings);
    if (program == 3u) {
        out.position = tree_billboard(settings, position, tangent.xy, uv0, uv2.x, axes, view, false);
        out.normal = mix(settings.lod2_normal.xyz, normalize(normal), settings.shade.x);
        out.uv = uv1;
    } else if (program == 1u && settings.tree.y > 0.5) {
        // The cards: the model's offset from the card's centre, its Z up made the game's (x, -z, y), along the
        // view's rows, back at the centre.
        var rows: mat3x3f;
        if (settings.tree.y < 1.5) {
            rows = mat3x3f(view.right, -view.up, view.back);
        } else {
            rows = mat3x3f(tree_across(view.back, view.right), vec3f(0.0, 0.0, -1.0), tree_across(view.right, view.back));
        }
        let towards = vec3f(uv1, uv2.x);
        let offset = -towards;
        out.position = rows * vec3f(offset.x, -offset.z, offset.y) + position + towards;
        out.normal = rows * vec3f(normal.x, -normal.z, normal.y);
    }
    return out;
}

// A billboard's corner: its middle, plus where it is on the quad across the view (or upright, as alignment says), by
// treeLod2Params and its size; in the cascades, by the same and pushed away from the sun by 0.3 of its width. Then turned
// back by the placement's rotation (dot with each axis), which its matrix applies again.
fn tree_billboard(settings: TreeSettings, middle: vec3f, size: vec2f, corner: vec2f, alignment: f32, axes: mat3x3f, view: TreeView,
    cascade: bool) -> vec3f {
    let extent = settings.size.xy * size;
    let uvs = (corner - 0.5) * extent;
    let across = mix(view.right, tree_across(view.back, view.right), alignment);
    let down = mix(-view.up, vec3f(0.0, 0.0, -1.0), alignment);
    var offset = uvs.x * across + uvs.y * down;
    if (cascade) {
        offset -= 0.3 * extent.x * view.back;
    }
    offset += middle;
    return vec3f(dot(offset, axes[0]), dot(offset, axes[1]), dot(offset, axes[2]));
}

// A tree's vertex in its model's space for the sun's shadow, seen along view (the sun's): the billboards spread across it,
// the cards as they are, pushed along its back by ShadowFacingOffset; the rest as they are.
fn tree_caster(settings: TreeSettings, position: vec3f, tangent: vec4f, uv0: vec2f, uv1: vec2f, uv2: vec2f, axes: mat3x3f,
    view: TreeView) -> TreeVertex {
    var out: TreeVertex;
    out.position = position;
    out.normal = vec3f(0.0, 0.0, 1.0);
    out.uv = uv0;
    let program = tree_program(settings);
    if (program == 3u) {
        out.position = tree_billboard(settings, position, tangent.xy, uv0, uv2.x, axes, view, true);
        out.uv = uv1;
    } else if (program == 1u && settings.tree.y > 0.5) {
        out.position = position + view.back * settings.size.z;
    }
    return out;
}

// The sun's view's axes from a cascade's matrix: its clip X and Y rows across, its Z row (depth grows away from the
// sun) behind.
fn tree_shadow_view(matrix: mat4x4f) -> TreeView {
    var view: TreeView;
    view.right = normalize(vec3f(matrix[0].x, matrix[1].x, matrix[2].x));
    view.up = normalize(vec3f(matrix[0].y, matrix[1].y, matrix[2].y));
    view.back = -normalize(vec3f(matrix[0].z, matrix[1].z, matrix[2].z));
    return view;
}

// The game's sub-sampled alpha dither, as Enhanced's _SubSampleAlpha tree programs read it without their TAA's frame
// offset: a 3x3 pattern over the frame's pixels, a pixel dropped where its value is above the coverage.
fn tree_dither(pixel: vec2f) -> f32 {
    var pattern = array<f32, 9>(0.9, 0.2, 0.5, 0.3, 0.6, 0.7, 0.4, 0.8, 0.1);
    let cell = vec2u(max(pixel, vec2f(0.0))) % vec2u(3u);
    return pattern[cell.x + 3u * cell.y];
}

// The cascades' alpha test for trees (PS_CascadeShadows_texture with its tree boost): twice the diffuse alpha, less
// 90/255, kept from nought.
fn tree_casts(alpha: f32) -> bool {
    return alpha * 2.0 - 90.0 / 255.0 >= 0.0;
}
