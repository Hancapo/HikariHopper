// The light drawables are shaded with, shared by surface.wgsl, ground_shadow.wgsl, bloom.wgsl and tonemap.wgsl:
// GTA V's timecycle light at the viewer's time of day (Timecycle.cs), the exposure, bloom and filmic curve its
// post-process settles on, the directional light's shadow map, the reflection map (reflection_map.wgsl), and the local
// lights. Put after scene.wgsl, as group 2.

struct Light {
    // Unit direction towards the directional light (the sun, or the moon).
    sun_direction: vec4f,
    // The directional light's colour times its multiplier, as the directional pass reads it.
    sun_colour: vec4f,
    // The natural ambient's down light (from the sky, on what faces up), with the down wrap in w.
    natural_down: vec4f,
    // The natural ambient's up light (from the ground, on what faces down), with the props' occlusion multiplier in w.
    natural_up: vec4f,
    // The directional ambient's colour times its intensity, with the vehicles' occlusion multiplier in w.
    directional_ambient: vec4f,
    // The way the directional ambient comes from, a unit vector.
    directional_ambient_direction: vec4f,
    // The exposure curve: scale, power and offset (visualsettings.dat), and the timecycle's exposure plus the viewer's.
    exposure_curve: vec4f,
    // The timecycle's exposure range, then TonemapParams: how far the filmic curve is from the bright one to the dark
    // one is saturate(exposure * z + w), one over the dark exposure less the bright one, and minus the bright one over that.
    exposure_range: vec4f,
    // The bright and dark filmic curves: A, B, C, D; then E, F and the white point W. The bright one's w is
    // PS_Composite's Gamma, which the tone-mapped colour is raised to: one over the display gamma.
    filmic_bright0: vec4f,
    filmic_bright1: vec4f,
    filmic_dark0: vec4f,
    filmic_dark1: vec4f,
    // The reflection map's mip count (gReflectionMipCount), in x; the day/night balance (globalScalars2.y: nought by
    // day, one while the night emissives are on, NightEmissives.cs), in y.
    reflection: vec4f,
    // From origin-relative positions to each of the shadow's cascades' clip space (ViewportRenderer.FitCascades).
    shadow: array<mat4x4f, 4>,
    // How far from the camera, along its view, each cascade reaches; past the last, nothing casts.
    shadow_splits: vec4f,
    // Each cascade's texel in metres.
    shadow_texels: vec4f,
    // Each cascade's depth range in metres.
    shadow_depths: vec4f,
    // The cascades' resolution in z, and whether shadows are on in w.
    shadow_parameters: vec4f,
    // The bloom (BloomParams): the exposed luminance its bright pass starts at, the timecycle's bloom intensity, and
    // one over the luminance range it fades in over.
    bloom: vec4f,
    // The peds' ambient occlusion multiplier (light_amb_occ_mult_ped) and their rim light's (light_ped_rim_mult).
    ped: vec4f,
    // The artificial ambient outside interiors, on what faces up and on what faces down (light_artificial_ext_*).
    artificial_down: vec4f,
    artificial_up: vec4f,
    // The water's: seconds since the view began (its ripples' time), the timecycle's water_foglight, and the frame's
    // near plane (depth is near over distance); then the level of detail fades' clock (lod_fade_keeps).
    water: vec4f,
};

@group(2) @binding(0) var<uniform> light: Light;
@group(2) @binding(1) var shadow_map: texture_depth_2d_array;
@group(2) @binding(2) var shadow_compare: sampler_comparison;
@group(2) @binding(3) var reflection_map: texture_2d<f32>;
@group(2) @binding(4) var reflection_sampler: sampler;

// The local lights drawables are lit by (ViewportRenderer.LocalLights.cs): a model's own in the 3D viewer, the cut's in
// a world. At most 512 nearest the camera that reach the screen, each a position relative to the scene's origin and
// its radius, its colour times its intensity and its type (1 a spot, 2 a capsule), its direction and a spot's inner
// cosine, then its outer cosine, falloff exponent and a capsule's length; and a 32 by 18 grid of the screen, each
// tile's lights as a 512-bit mask.
struct LocalLight {
    position_radius: vec4f,
    colour_type: vec4f,
    direction_inner: vec4f,
    outer_falloff: vec4f,
};

@group(2) @binding(5) var<storage, read> local_lights: array<LocalLight, 512>;
@group(2) @binding(6) var<storage, read> local_light_tiles: array<u32, 9216>;

// The scene behind the water, copied before water is drawn (ViewportRenderer.Water.cs): its colour and its depth.
@group(2) @binding(7) var water_background: texture_2d<f32>;
@group(2) @binding(8) var water_depth: texture_depth_2d;

// Set by a fragment entry point that takes the local lights: the pixel and the point being shaded, relative to the
// scene's origin. Left off where the screen's tiles don't apply (the reflection map).
var<private> local_lights_on: bool = false;

// The interior room the point being shaded is in (world_instances.wgsl's instance_room), set by its fragment entry
// point: nought outside, and in the viewer.
var<private> shaded_room: vec4f = vec4f(0.0);

fn use_room(room: vec4f) {
    shaded_room = room;
}

// A colour packed a byte a channel over 0 to 4 (InteriorAmbient.Pack).
fn unpack_ambient(packed: f32) -> vec3f {
    let bits = u32(packed + 0.5);
    return vec3f(f32(bits & 255u), f32((bits >> 8u) & 255u), f32((bits >> 16u) & 255u)) * (4.0 / 255.0);
}
var<private> local_pixel: vec2f;
var<private> local_place: vec3f;

fn use_local_lights(pixel: vec2f, place: vec3f) {
    local_lights_on = true;
    local_pixel = pixel;
    local_place = place;
}

// Where the tile holding the pixel being shaded starts in the masks, sixteen words of them: past the end (no lights)
// unless use_local_lights was called.
fn local_light_tile() -> u32 {
    if (!local_lights_on) {
        return 9216u;
    }
    // The tiles cover the frame the surfaces are drawn into (scene.viewport.zw).
    let tile = min(vec2u(local_pixel / max(scene.viewport.zw, vec2f(1.0)) * vec2f(32.0, 18.0)), vec2u(31u, 17u));
    return (tile.y * 32u + tile.x) * 16u;
}

// One word of the pixel's tile's mask: the lights 32 * word to 32 * word + 31 that reach it.
fn local_light_word(tile: u32, word: u32) -> u32 {
    if (tile >= 9216u) {
        return 0u;
    }
    return local_light_tiles[tile + word];
}

// GTA's local lights over what a deferred shader wrote, as PS_pointCM_standard and PS_spotCM_standard light it (in
// Enhanced's programs, Legacy's alike), without their shadows, occlusion and contact shadows:
//   - Falloff: a = saturate(1 - d^2 / r^2), shaped by a / ((1 - k) a + k), Schlick's stand-in for a^k with k the light's
//     falloff exponent.
//   - A spot's cone: linear from its outer cosine to its inner one (gLightConeScale and gLightConeOffset).
//   - A capsule (PS_capsule's): lit from the nearest point of its segment, along its direction, its middle at its
//     position.
//   - Then the directional pass's own arithmetic: the diffuse less what the surface reflects, normalised Blinn-Phong.
// Where a light shines from, relative to the scene's origin: a capsule from the nearest point of its segment.
fn local_light_point(source: LocalLight) -> vec3f {
    if (source.colour_type.w < 1.5) {
        return source.position_radius.xyz;
    }
    let extent = source.outer_falloff.z;
    let axis = source.direction_inner.xyz;
    let start = source.position_radius.xyz - axis * (extent * 0.5);
    return start + axis * (saturate(dot(local_place - start, axis) / max(extent, 1e-4)) * extent);
}

fn gta_local_lights(albedo: vec3f, kept: f32, spec: f32, exponent: f32, fresnel_value: f32, normal: vec3f, view: vec3f) -> vec3f {
    let tile = local_light_tile();
    var radiance = vec3f(0.0);
    for (var word = 0u; word < 16u; word++) {
        var mask = local_light_word(tile, word);
        while (mask != 0u) {
            let index = word * 32u + firstTrailingBit(mask);
            mask &= mask - 1u;
            let source = local_lights[index];
            let delta = local_light_point(source) - local_place;
            let distance2 = dot(delta, delta);
            let radius = source.position_radius.w;
            let a = saturate(1.0 - distance2 / max(radius * radius, 1e-6));
            if (a <= 0.0) { continue; }
            let k = max(source.outer_falloff.y, 1e-3);
            var attenuation = a / ((1.0 - k) * a + k);
            let to_light = delta * inverseSqrt(max(distance2, 1e-8));
            if (source.colour_type.w > 0.5 && source.colour_type.w < 1.5) {
                let cosine = dot(to_light, -source.direction_inner.xyz);
                let inner = source.direction_inner.w;
                let outer = source.outer_falloff.x;
                attenuation *= saturate((cosine - outer) / max(inner - outer, 1e-4));
            }
            if (attenuation < 1e-6) { continue; }
            let half_vector = normalize(to_light + view);
            let n_dot_l = saturate(dot(normal, to_light));
            let h_dot_l = saturate(dot(half_vector, to_light));
            let n_dot_h = saturate(dot(normal, half_vector));
            let highlight = (exponent + 2.0) / 8.0 * pow(n_dot_h + 1e-8, exponent) * fresnel(fresnel_value, h_dot_l);
            radiance += source.colour_type.rgb * attenuation * n_dot_l * (albedo * kept + vec3f(spec * highlight));
        }
    }
    return radiance;
}

// The exposure in stops for the frame's measured mean luminance (exposure.wgsl), as PS_CalculateExposure works it
// out: scale * L^power + offset + the timecycle's exposure, kept in the timecycle's range.
fn exposure_stops(mean: f32) -> f32 {
    let curve = light.exposure_curve;
    let range = light.exposure_range;
    return clamp(curve.x * pow(max(mean, 1e-6), curve.y) + curve.z + curve.w, range.x, range.y);
}

// How far the filmic curve has moved from the bright one to the dark one at this exposure.
fn filmic_blend(stops: f32) -> f32 {
    let range = light.exposure_range;
    return saturate(stops * range.z + range.w);
}

// PS_directional_ambient's natural ambient for a unit normal: the down light on what faces up, the up light on what
// faces down, the down light wrapping past the horizon by the down wrap; and the directional ambient on what faces it.
fn gta_ambient(n: vec3f) -> vec3f {
    let wrap = light.natural_down.w;
    let h = max(0.0, (n.z + wrap) / (1.0 + wrap));
    // An interior room keeps only so much of the natural ambient.
    let room_natural = select(1.0, shaded_room.z, shaded_room.w > 0.5);
    let natural = mix(light.natural_up.rgb, light.natural_down.rgb, h) * room_natural;
    let directional = light.directional_ambient.rgb * saturate(dot(light.directional_ambient_direction.xyz, n));
    return natural + directional;
}

// PS_directional_ambient's artificial ambient for this normal, from above and below as the natural one: an interior
// room's own (its timecycle modifiers'), or the weather's outside interiors (the game tells them apart by a stencil
// bit its interiors set).
fn gta_artificial_ambient(n: vec3f) -> vec3f {
    let wrap = light.natural_down.w;
    let h = max(0.0, (n.z + wrap) / (1.0 + wrap));
    if (shaded_room.w > 0.5) {
        return mix(unpack_ambient(shaded_room.y), unpack_ambient(shaded_room.x), h);
    }
    return mix(light.artificial_up.rgb, light.artificial_down.rgb, h);
}

// How long a placement takes to fade in or out as the levels of detail change, in seconds.
const LOD_FADE_SECONDS: f32 = 0.4;

// Whether a pixel of a placement fading between levels of detail is drawn (after GTA's dithered entity fade): one fading
// in is drawn on more of its pixels each moment, one fading out on fewer, by a 4x4 ordered dither, so a model and the
// ones replacing it, which began together, share the pixels between them as one gives way to the other. fade is when
// the fade began on the fades' clock (water.w) and which way, 1 in, -1 out, 0 none.
fn lod_fade_keeps(pixel: vec2f, fade: vec2f) -> bool {
    if (abs(fade.y) < 0.5) {
        return true;
    }
    let t = saturate((light.water.w - fade.x) / LOD_FADE_SECONDS);
    var bayer = array<f32, 16>(0.0, 8.0, 2.0, 10.0, 12.0, 4.0, 14.0, 6.0, 3.0, 11.0, 1.0, 9.0, 15.0, 7.0, 13.0, 5.0);
    let cell = vec2u(max(pixel, vec2f(0.0))) % vec2u(4u);
    let threshold = (bayer[cell.y * 4u + cell.x] + 0.5) / 16.0;
    return select((threshold >= t), (threshold < t), (fade.y > 0.0));
}

// The key light's shadow at a point, from the cascade its distance from the camera falls in, as RedFury's cascade_pcf
// reads one: nine bilinear comparison taps one
// texel apart (a smooth 4x4 tent), offset along the normal by the texel's footprint, which keeps grazing surfaces
// from shadowing themselves in stripes. n is the geometric normal of the side being shaded (the back of a surface
// drawn from both sides faces the other way). A side facing away from the sun is offset away from it too, into its
// own shadow, and fades out across the terminator: its bump-mapped normals can still face the sun, but no sunlight
// reaches it.
fn sun_shadow(p: vec3f, n: vec3f) -> f32 {
    let sun = light.sun_direction.xyz;
    let facing = dot(n, sun);
    let terminator = saturate(facing * 10.0 + 0.5);
    if (light.shadow_parameters.w < 0.5 || terminator <= 0.0) {
        return terminator;
    }
    // The cascade: the first that reaches as far from the camera as the point is (a perspective clip w is the depth
    // along the view).
    let distance = (scene.view_projection * vec4f(p, 1.0)).w;
    var cascade = 3u;
    if (distance < light.shadow_splits.z) {
        cascade = 2u;
    }
    if (distance < light.shadow_splits.y) {
        cascade = 1u;
    }
    if (distance < light.shadow_splits.x) {
        cascade = 0u;
    }
    let texel_world = light.shadow_texels[cascade];
    let offset = p + n * texel_world * (0.75 + 1.75 * (1.0 - abs(facing))) + sun * texel_world * 0.5 * saturate(facing * 10.0);
    let clip = light.shadow[cascade] * vec4f(offset, 1.0);
    let uvz = vec3f(clip.x * 0.5 + 0.5, 0.5 - clip.y * 0.5, clip.z);
    if (any(uvz.xy <= vec2f(0.0)) || any(uvz.xy >= vec2f(1.0)) || uvz.z >= 1.0 || uvz.z <= 0.0) {
        return terminator;
    }
    let texel = 1.0 / light.shadow_parameters.z;
    let reference = uvz.z - texel_world / light.shadow_depths[cascade];
    var sum = 0.0;
    for (var y = -1; y <= 1; y++) {
        for (var x = -1; x <= 1; x++) {
            sum += textureSampleCompareLevel(shadow_map, shadow_compare, uvz.xy + vec2f(f32(x), f32(y)) * texel, i32(cascade), reference);
        }
    }
    return sum / 9.0 * terminator;
}

// How deep under the sun's first caster a point is, as the game's PS_directional_scatter measures it for its foliage's
// scatter: the point pulled 0.073 m back along its normal, its depth in the cascade it falls in against the mean of the
// four texels around it, in the cascade's depth (0 to 1). Nought without shadows, or outside every cascade.
fn tree_scatter_depth(p: vec3f, n: vec3f) -> f32 {
    if (light.shadow_parameters.w < 0.5) {
        return 0.0;
    }
    let shrunk = p - 0.073 * n;
    let distance = (scene.view_projection * vec4f(shrunk, 1.0)).w;
    var cascade = 3u;
    if (distance < light.shadow_splits.z) {
        cascade = 2u;
    }
    if (distance < light.shadow_splits.y) {
        cascade = 1u;
    }
    if (distance < light.shadow_splits.x) {
        cascade = 0u;
    }
    let clip = light.shadow[cascade] * vec4f(shrunk, 1.0);
    let uvz = vec3f(clip.x * 0.5 + 0.5, 0.5 - clip.y * 0.5, clip.z);
    if (any(uvz.xy <= vec2f(0.0)) || any(uvz.xy >= vec2f(1.0)) || uvz.z >= 1.0 || uvz.z <= 0.0) {
        return 0.0;
    }
    let size = i32(light.shadow_parameters.z);
    let texel = clamp(vec2i(uvz.xy * light.shadow_parameters.z), vec2i(1), vec2i(size - 2));
    let layer = i32(cascade);
    let mean = 0.25 * (textureLoad(shadow_map, texel - vec2i(1, 0), layer, 0) + textureLoad(shadow_map, texel + vec2i(1, 0), layer, 0)
        + textureLoad(shadow_map, texel - vec2i(0, 1), layer, 0) + textureLoad(shadow_map, texel + vec2i(0, 1), layer, 0));
    return abs(uvz.z - mean);
}

// Where PS_directional_ambient looks a direction up in the reflection map: a dual paraboloid, the upper hemisphere on
// the right half of the map and the lower on the left.
fn paraboloid_uv(r: vec3f) -> vec2f {
    let p = r.xy / (abs(r.z) + 1.0);
    let u = select(0.25 - 0.25 * p.x, 0.75 + 0.25 * p.x, r.z > 0.0);
    return vec2f(u, 0.5 - 0.5 * p.y);
}

// PS_directional_ambient's reflection: the map in the reflected direction, at a mip chosen by gloss (the specular
// exponent over 1500) with the game's curve, times what the surface reflects at this angle (fresnel times specular
// intensity), and an energy term that runs from 1/pi for dull surfaces to one for glossy ones (the exponent over 563).
// The caller scales it by the ambient scale, as the game does.
fn gta_reflection(normal: vec3f, view: vec3f, exponent: f32, reflected_share: f32) -> vec3f {
    let direction = reflect(-view, normal);
    let rough = 1.0 - saturate(exponent / 1500.0);
    let mips = light.reflection.x;
    let level = select(rough * rough * 5.0 + mips - 5.0, rough * mips - 5.0, rough * mips < mips - 5.0);
    var environment = textureSampleLevel(reflection_map, reflection_sampler, paraboloid_uv(direction), max(level, 0.0)).rgb;
    // Inside an interior the game's reflection map, drawn from the camera, holds the room; the viewer's holds only the
    // sky, so a room reflects its own artificial ambient in its place.
    if (shaded_room.w > 0.5) {
        environment = gta_artificial_ambient(direction);
    }
    let energy = 0.31830987 + 0.68169010 * saturate(exponent / 563.0);
    return environment * reflected_share * energy;
}

// Schlick's approximation as GTA writes it: the fresnel constant is how much of the light it lets through, so F0
// is one less it.
fn fresnel(fresnel_value: f32, cosine: f32) -> f32 {
    let m = 1.0 - cosine;
    let m2 = m * m;
    return (1.0 - fresnel_value) + fresnel_value * m * m2 * m2;
}

// GTA's directional pass (PS_directional_just_dir) and the core of its ambient one (PS_directional_ambient) over what
// a deferred shader wrote to its G-buffer: albedo as stored (gamma 2, squared here as the passes read it), normal,
// specular intensity, power and fresnel, and the ambient scale. The game's lighting has no 1/pi.
fn gta_lit(stored: vec3f, normal: vec3f, view: vec3f, intensity: f32, power: f32, fresnel_value: f32, ambient_scale: f32,
    shadow: f32, reflection_scale: f32, artificial_scale: f32) -> vec3f {
    let to_light = normalize(light.sun_direction.xyz);
    let albedo = stored * stored;
    let spec = saturate(intensity);
    // GTA's exponent from the stored power: three times it up to 500, steeply beyond. The G-buffer is 8-bit and holds
    // the square root of the power over 512, so no power goes past 512.
    let p = clamp(power, 0.0, 512.0);
    let exponent = 3.0 * min(p, 500.0) + 558.0 * max(0.0, p - 500.0);

    let half_vector = normalize(to_light + view);
    let n_dot_l = saturate(dot(normal, to_light));
    let n_dot_v = saturate(dot(normal, view));
    let h_dot_l = saturate(dot(half_vector, to_light));
    let n_dot_h = saturate(dot(normal, half_vector) + 1e-8);

    // Diffuse loses what the surface reflects at this angle; specular is normalised Blinn-Phong.
    let kept = 1.0 - fresnel(fresnel_value, n_dot_v) * spec;
    let highlight = (exponent + 2.0) / 8.0 * pow(n_dot_h, exponent + 1e-8) * fresnel(fresnel_value, h_dot_l);
    let sun = light.sun_colour.rgb * shadow * (albedo * kept * n_dot_l + vec3f(n_dot_l * spec * highlight));

    // Ambient: the timecycle's natural and directional ambient for this normal, by the ambient scale, and its
    // artificial ambient by the artificial scale (what the vertex green stores).
    let ambient = albedo * kept * (gta_ambient(normal) * ambient_scale + gta_artificial_ambient(normal) * artificial_scale);

    // Reflection: what the surface reflects (the share the diffuse loses), scaled as the ambient is, and by the scale
    // the G-buffer's last channel holds (one for all but peds, whose deferred shaders write their own).
    let reflection = gta_reflection(normal, view, exponent, 1.0 - kept) * ambient_scale * reflection_scale;

    // The local lights, lit as the sun is, after their falloff.
    let local = gta_local_lights(albedo, kept, spec, exponent, fresnel_value, normal, view);
    return sun + ambient + reflection + local;
}
