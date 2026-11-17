// GTA V's coronas: the glow it draws on its lights, each a camera-facing sprite of graphics.ytd's "corona",
// added over the world after it's drawn, as the game draws them (before the lens flare). One instanced
// draw: each corona's first layer, its second (the next-gen coronas turn the two opposite ways), then its anamorphic
// flare ("flare_animorphic"); four corners each.
//   - What the game registers each corona with (a map light or
//     a LOD light) and how it then sizes, biases towards the camera and fades it
//     are worked out here per vertex from the camera, which the game does on the CPU each
//     frame: the lights' own values come once, as they're bound.
//   - Then the im effect's VS_corona and PS_corona as Enhanced has them: how much of the corona the scene's depth hides round
//     its middle (24 taps of a Poisson disc), its fade at the screen's edges and near the camera, the fog over it (the
//     game's height fog, haze and all), and the texture squared twice.
//   - Left out: Enhanced's second layer of occluders (a depth and its opacity; the scene's depth alone here), the
//     coronas in reflections and under water.
// Put after scene.wgsl, sky_system.wgsl and gta_sky.wgsl: the fog's constants are the sky's.

struct CoronaFrame {
    // The view's third row (gWorldView[2]), which the game's near fade measures with.
    view_row: vec4f,
    // The slow and fast rotations, the screen's size in the world a metre away per pixel, m_doNGCoronas.
    rotation: vec4f,
    // The sprites' size multiplier (2.5 times sprite_size), sprite_brightness, the screen-space expansion, the LOD
    // lights' corona size (times the size multiplier).
    sprite: vec4f,
    // sizeScaleGlobal, intensityScaleGlobal, the two layers' multipliers.
    scales: vec4f,
    // The z-bias's clamp, its multiplier far off, and how much of a corona's final size it is.
    zbias: vec4f,
    // Where the z-bias's multiplier starts and reaches its full, the LOD lights' z-bias, and the sun-lit lights' scale.
    zbias_far: vec4f,
    // m_scaleRampUp, the cosine of its angle, flareMinAngleDegToFadeIn in radians, screenEdgeMinDistForFade.
    ramp: vec4f,
    // occlusionSizeScale, occlusionSizeMax, one over the target's width.
    occlusion: vec4f,
    // dir.mult.fadeoff start and distance, light_falloff_mult, the LOD lights' corona intensity multiplier.
    fade: vec4f,
    // The small lights' reach and fade range, squared; lodlight.corona.size.
    small: vec4f,
    // Each LOD light category's corona range, squared.
    corona_range: vec4f,
    // flareCoronaSizeRatio, its horizontal and vertical screen-centre size multipliers.
    flare: vec4f,
    flare_shift: vec4f,
    base_value: vec4f,
    // How many coronas there are.
    count: vec4u,
    spare: array<vec4f, 3>,
};

// A corona as it's registered (GtaCoronaInstance).
struct Corona {
    position: vec3f,
    // Its flags, then its cone's inner and outer angles in degrees.
    packed: u32,
    direction: vec3f,
    colour: u32,
    size: f32,
    intensity: f32,
    falloff: f32,
    extent: f32,
    kind: u32,
    fade_distance: f32,
    light_flags: u32,
    light_intensity: f32,
};

@group(1) @binding(8) var<uniform> frame: CoronaFrame;
@group(1) @binding(9) var<storage, read> coronas: array<Corona>;
@group(1) @binding(10) var scene_depth: texture_depth_2d;
@group(1) @binding(11) var corona_map: texture_2d<f32>;
@group(1) @binding(12) var flare_map: texture_2d<f32>;
@group(1) @binding(13) var corona_sampler: sampler;

// The corona's flags.
const HAS_DIRECTION: u32 = 2u;
const DONT_RENDER_FLARE: u32 = 16u;
const FADEOFF_DIR_MULT: u32 = 32u;
const DONT_ROTATE: u32 = 128u;
const DONT_RAMP_UP: u32 = 256u;

// What it's registered from (GtaCoronas.Kind*).
const KIND_LOD: u32 = 1u;
const KIND_INTERIOR: u32 = 8u;
const KIND_FIXED_SIZE: u32 = 16u;
const KIND_FROM_SUN: u32 = 32u;
const KIND_CORONA_ONLY: u32 = 64u;

// The lights' flags its category is read from.
const LIGHT_FAR_LOD_LIGHT: u32 = 4194304u;
const LIGHT_FORCE_MEDIUM_LOD_LIGHT: u32 = 268435456u;

// The im effect's Poisson disc: two taps a row.
const POISSON = array<vec4f, 12>(
    vec4f(0.7729676, -0.2221565, 0.5282406, -0.8028475),
    vec4f(0.2629631, -0.07523455, 0.8237055, -0.5561877),
    vec4f(0.5696832, 0.121086, 0.3988842, -0.4114171),
    vec4f(0.4400159, 0.4879902, -0.03464016, 0.176342),
    vec4f(-0.1203233, -0.5860488, 0.2004656, -0.6874188),
    vec4f(-0.258614, -0.1630804, -0.6883209, -0.6355559),
    vec4f(-0.3450861, -0.8480957, -0.3628126, 0.6144199),
    vec4f(0.06174967, 0.5069199, 0.9773164, 0.2086624),
    vec4f(0.7894329, 0.4902646, -0.7023129, -0.07146062),
    vec4f(-0.4953414, 0.233821, 0.1793105, 0.9625847),
    vec4f(-0.9309751, -0.327397, -0.9129849, 0.2416974),
    vec4f(-0.2172657, 0.9727083, -0.6971195, 0.5296693),
);

struct CoronaVarying {
    @builtin(position) position: vec4f,
    @location(0) uv: vec2f,
    @location(1) colour: vec3f,
    // One for a flare: its texture, and no base value.
    @location(2) @interpolate(flat) flare: u32,
};

fn corona_hidden() -> CoronaVarying {
    var out: CoronaVarying;
    out.position = vec4f(2.0, 2.0, 0.5, 1.0);
    out.uv = vec2f(0.0);
    out.colour = vec3f(0.0);
    out.flare = 0u;
    return out;
}

fn corona_clip(world: vec3f) -> vec4f {
    return scene.view_projection * vec4f(world - scene.origin.xyz, 1.0);
}

// How far a small light's corona has faded with distance, as its LOD light's would.
fn small_light_fade(distance_squared: f32) -> f32 {
    return saturate((distance_squared - (frame.small.x - frame.small.y)) / frame.small.y);
}

// The light's category, as the game sorts its lights.
fn light_category(kind: u32, radius: f32, intensity: f32, extent: f32, flags: u32) -> u32 {
    if ((flags & LIGHT_FAR_LOD_LIGHT) != 0u) {
        return 2u;
    }
    var length = radius;
    if (((kind >> 8u) & 255u) == 4u) {
        length = 2.0 * radius + extent;
    }
    if ((flags & LIGHT_FORCE_MEDIUM_LOD_LIGHT) != 0u || (length >= 10.0 && intensity >= 1.0)) {
        return 1u;
    }
    return 0u;
}

// The fog's amount: the height fog, then the haze over what it leaves.
fn corona_fog(ray: vec3f) -> f32 {
    let ground = gta_ground_fog(ray);
    let amount = ground.x * sky.fog[2].y;
    let haze = (1.0 - amount) * sky.fog[1].y * (1.0 - exp(sky.fog[1].x * max(0.0, ground.y - sky.fog[2].x)));
    return saturate(amount + haze);
}

// Whether the scene at a point of the screen (in NDC) is no nearer than depth (near over distance, reversed Z).
fn corona_tap(ndc: vec2f, depth: f32) -> f32 {
    let size = vec2f(textureDimensions(scene_depth));
    let uv = ndc * vec2f(0.5, -0.5) + 0.5;
    let texel = vec2i(clamp(floor(uv * size), vec2f(0.0), size - 1.0));
    return select(0.0, 1.0, textureLoad(scene_depth, texel, 0) <= depth);
}

@vertex
fn vs_gta_corona(@builtin(vertex_index) vertex: u32, @builtin(instance_index) instance: u32) -> CoronaVarying {
    let count = max(frame.count.x, 1u);
    let layer = instance / count;
    let c = coronas[instance % count];
    var flags = c.packed & 65535u;
    let eye = scene.eye.xyz;
    let to_eye = eye - c.position;
    let distance_squared = dot(to_eye, to_eye);
    let distance = sqrt(distance_squared);
    let fade_out = saturate((distance - frame.fade.x) / frame.fade.y);
    let spot = (flags & HAS_DIRECTION) != 0u;

    // The corona as the game registers it.
    var size: f32;
    var intensity: f32;
    var zbias: f32;
    if ((c.kind & KIND_LOD) != 0u) {
        let category = (c.kind >> 1u) & 3u;
        if (!(distance_squared <= frame.corona_range[category])) {
            return corona_hidden();
        }
        intensity = c.intensity * frame.sprite.y * frame.fade.w;
        if (category == 0u) {
            intensity *= 1.0 - small_light_fade(distance_squared);
        }
        if (spot) {
            // Facing away, it isn't registered at all.
            let forward = mix(to_eye / distance, c.direction, fade_out);
            if (!(intensity * dot(c.direction, forward) > 0.0)) {
                return corona_hidden();
            }
        }
        size = frame.sprite.w;
        zbias = frame.zbias_far.z;
    } else {
        let interior = (c.kind & KIND_INTERIOR) != 0u;
        var light = c.light_intensity;
        if ((c.kind & KIND_FROM_SUN) != 0u) {
            light *= frame.zbias_far.w;
        }
        if (!(light > 0.0)) {
            return corona_hidden();
        }
        var size_multiplier = 2.5;
        var intensity_multiplier = light;
        if (!interior) {
            size_multiplier = frame.sprite.x;
            intensity_multiplier *= frame.sprite.y;
        }
        let radius = c.falloff * select(1.0, frame.fade.z, !interior && (c.kind & KIND_CORONA_ONLY) == 0u);
        let category = light_category(c.kind, radius, light, c.extent, c.light_flags);
        if (spot && category > 0u) {
            flags |= FADEOFF_DIR_MULT;
        }
        let light_fade = saturate((c.fade_distance - distance) / 10.0);
        if ((c.kind & KIND_FIXED_SIZE) != 0u) {
            size = c.size * size_multiplier;
            intensity_multiplier *= light_fade;
        } else {
            if (category == 0u) {
                intensity_multiplier *= 1.0 - small_light_fade(distance_squared);
            }
            // Towards the LOD light's size as the map light fades out to it.
            let towards = saturate((light_fade - 0.75) / 0.25);
            size = mix(frame.small.z, c.size, towards) * size_multiplier;
        }
        zbias = size * frame.zbias.w;
        intensity = c.intensity * intensity_multiplier;
        if (!(intensity > 0.0 && size > 0.0)) {
            return corona_hidden();
        }
    }

    // None for a light off the screen.
    let light_clip = corona_clip(c.position);
    if (!(light_clip.w > 0.0 && abs(light_clip.x) <= light_clip.w && abs(light_clip.y) <= light_clip.w)) {
        return corona_hidden();
    }
    var final_intensity = intensity;
    var fade_for_size = 1.0;
    var fade_for_flare = 1.0;
    if (spot) {
        let inner = cos(radians(f32((c.packed >> 16u) & 255u)));
        let outer = cos(radians(f32(c.packed >> 24u)));
        let facing = dot(c.direction, to_eye / distance);
        fade_for_size = saturate(saturate((facing - inner) / (1.0001 - inner)) + saturate((facing - outer) / (1.0001 - outer)));
        // Its view threshold is one: no remapping.
        var fade_for_intensity = fade_for_size;
        if ((flags & FADEOFF_DIR_MULT) != 0u) {
            fade_for_intensity = mix(fade_for_intensity, 1.0, fade_out);
            fade_for_size = mix(fade_for_size, 1.0, fade_out);
        }
        final_intensity *= fade_for_intensity * frame.scales.y;
        fade_for_flare = fade_for_size;
    }
    if (!(final_intensity > 0.0)) {
        return corona_hidden();
    }
    let camera_to = c.position - eye;
    let z = dot(camera_to, scene.forward.xyz);
    var final_size = size * frame.scales.x;
    var bias = clamp(zbias, frame.zbias.x, frame.zbias.y) / z;
    bias *= 1.0 + (frame.zbias.z - 1.0) * clamp((z - frame.zbias_far.x) / (frame.zbias_far.y - frame.zbias_far.x), 0.0, 1.0);
    let expansion = mix(frame.sprite.z, 0.0, fade_out);
    final_size *= z * (expansion * frame.rotation.z + 1.0 / (z + max(0.0, z)));
    final_size *= fade_for_size;
    let position = c.position - camera_to * bias;
    final_size -= final_size * bias;
    if (!(final_size > 0.0)) {
        return corona_hidden();
    }
    let render_flare = spot && fade_for_flare >= frame.ramp.z && (flags & DONT_RENDER_FLARE) == 0u;
    var rotation_blend = 0.0;
    if (spot && (flags & DONT_RAMP_UP) == 0u && frame.rotation.w > 0.5) {
        let along = dot(c.direction, to_eye);
        if (along > 0.0) {
            rotation_blend = clamp((along / distance - frame.ramp.y) / (1.0 - frame.ramp.y), 0.0, 1.0);
            final_size *= mix(1.0, frame.ramp.x, rotation_blend);
        }
    }

    // Its corners: right and the camera's up as the game has it (screen down), with the texture's corner at each.
    let corner = vec2f(f32(vertex & 1u), f32(vertex >> 1u)) * 2.0 - 1.0;
    var uv = vec2f(corner.x * 0.5 + 0.5, corner.y * 0.5 + 0.5);
    let camera_up = -scene.up.xyz;
    var right = scene.right.xyz * final_size;
    var up = camera_up * final_size;
    let rgb = vec3f(f32(c.colour & 255u), f32((c.colour >> 8u) & 255u), f32((c.colour >> 16u) & 255u));
    var colour: vec3f;
    var flare = 0u;
    if (layer == 2u) {
        // Each flare: stretched across the screen the further it is from the middle, sideways or up and down by
        // which quarter it's in, its colour shifted to blue, faded in as it faces the camera.
        if (!render_flare) {
            return corona_hidden();
        }
        flare = 1u;
        let angle = saturate(dot(c.direction, normalize(eye - position)));
        let clip = corona_clip(position);
        let ndc = clip.xy / clip.w;
        var scalar = saturate(abs(abs(ndc.x) - abs(ndc.y)));
        scalar *= scalar;
        let rotated = vec2f(ndc.x * 0.707 + ndc.y * 0.707, ndc.y * 0.707 - ndc.x * 0.707);
        let same_sign = ((bitcast<u32>(rotated.x) ^ bitcast<u32>(rotated.y)) >> 31u) == 0u;
        let horizontal = 1.0 + select(scalar, 0.0, same_sign) * (frame.flare.y - 1.0);
        let vertical = 1.0 + select(0.0, scalar, same_sign) * (frame.flare.z - 1.0);
        right = scene.right.xyz * (final_size * frame.flare.x) * horizontal;
        up = camera_up * (final_size * frame.flare.x) * vertical;
        // A vertical flare's texture turned a quarter.
        if (vertical > 1.0) {
            uv = vec2f(uv.y, 1.0 - uv.x);
        }
        // The shift goes through the colour's bytes, as the game's Color32 does.
        colour = floor(rgb / 255.0 * frame.flare_shift.rgb * 255.0) / 255.0 * (final_intensity * clamp(angle - frame.ramp.z, 0.0, 1.0));
    } else {
        // Each corona: one layer, or two turning opposite ways for the next-gen coronas.
        let rotating = !((flags & DONT_ROTATE) != 0u || frame.rotation.w < 0.5);
        if (layer == 1u && !rotating) {
            return corona_hidden();
        }
        if (rotating) {
            let angle = (1.0 - rotation_blend) * frame.rotation.x + rotation_blend * frame.rotation.y;
            let s = select(1.0, -1.0, layer == 1u) * sin(angle);
            let k = cos(angle);
            let turned = k * right + s * up;
            up = k * up - s * right;
            right = turned;
        }
        colour = rgb / 255.0 * (select(frame.scales.z, frame.scales.w, layer == 1u) * final_intensity);
    }
    let world = position + corner.x * right + corner.y * up;

    // VS_corona: how much of it the scene hides round its middle, faded at the screen's edge and close to the camera.
    let half_size = length(world - position) * 0.707106781;
    var near_fade = 1.0;
    if (flare == 0u && length(position - eye) < half_size) {
        let along = dot(frame.view_row.xyz, position - eye);
        if (along > -half_size) {
            near_fade = saturate((1.0 - along / half_size) * 0.5);
        }
    }
    let middle_clip = corona_clip(position);
    let corner_clip = corona_clip(world);
    let over_w = 1.0 / middle_clip.w;
    let middle = middle_clip.xyz * over_w;
    let delta = corner_clip.xy * over_w - middle.xy;
    let radius = min(length(delta) * frame.occlusion.x, frame.occlusion.y * 2.0 * frame.occlusion.z);
    var shown = 0.0;
    for (var i = 0; i < 12; i++) {
        let taps = middle.xyxy + POISSON[i] * radius;
        shown += corona_tap(taps.xy, middle.z) + corona_tap(taps.zw, middle.z);
    }
    let edge = saturate(abs(middle.xy) - frame.ramp.w);
    let edge_fade = 1.0 - saturate(max(edge.x, edge.y) / (1.0001 - frame.ramp.w));
    var visible = shown / 24.0 * edge_fade;
    visible *= visible;
    var out: CoronaVarying;
    out.position = select(middle_clip, corner_clip, visible > 0.0);
    out.uv = uv;
    out.colour = colour * (near_fade * visible * (1.0 - corona_fog(world - eye)));
    out.flare = flare;
    return out;
}

// PS_corona: the texture's red squared twice, and the base value where it isn't black (none for the flares).
@fragment
fn fs_gta_corona(in: CoronaVarying) -> @location(0) vec4f {
    let glow = textureSample(corona_map, corona_sampler, in.uv).r;
    let streak = textureSample(flare_map, corona_sampler, in.uv).r;
    let t = select(glow, streak, in.flare == 1u);
    let base = select(1.0, 0.0, t < 1.0 / 255.0);
    let base_value = select(frame.base_value.rgb, vec3f(0.0), in.flare == 1u);
    let t2 = t * t;
    return vec4f(t2 * t2 * in.colour + base * base_value, base);
}
