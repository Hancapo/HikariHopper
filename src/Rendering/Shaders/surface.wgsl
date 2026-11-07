// Drawables, shaded as GTA V shades them. Two families of the game's deferred pixel shaders are rebuilt here, each
// writing what the game writes to its G-buffer (albedo, normal, specular intensity, power and fresnel, ambient scales):
//   - PS_DeferredTextured: default, normal, spec, normal_spec, emissive, decal, _tnt and the rest;
//   - PS_DeferredVehicleTextured: vehicle paint, mesh, shuts, tyres, interiors and the rest.
// That is then lit with the game's directional pass (PS_directional_just_dir) and the core of its ambient one
// (PS_directional_ambient). The game lights the G-buffer later; here both happen in one pass, with the same
// arithmetic. The light is the viewer's sky, after RedFury: the sun through the atmosphere, with its shadow, and the
// sky's irradiance (light.wgsl). The result is linear HDR with coverage, for tonemap.wgsl.
// Scene comes from scene.wgsl, the light from light.wgsl.

struct Material {
    // specularFresnel, specularFalloffMult, specularIntensityMult, bumpiness.
    specular: vec4f,
    // specMapIntMask, emissiveMultiplier.
    mask: vec4f,
    // Has a normal map, reads a specular map, alpha mode (0 opaque, 1 mask, 2 blend), is emissive.
    options: vec4f,
    // Alpha cutoff, has a tint palette, tintPaletteSelector, family (0 textured, 1 vehicle).
    extra: vec4f,
    // Vehicles: matDiffuseColor, matDiffuseSpecularRampEnabled.
    paint: vec4f,
    // Vehicles: dirtLevelMod (how dirty, how much of the dirt's colour, wet or ramp blend, second lobe strength).
    dirt_level: vec4f,
    // Vehicles: dirtColor, has a dirt map.
    dirt_colour: vec4f,
    // Vehicles: specular2Color_DirLerp, the second lobe's colour and how far its light swings overhead.
    specular2: vec4f,
    // Vehicles: is a paint, has the second lobe, has a diffuse ramp, takes specular from constants.
    vehicle: vec4f,
};

@group(1) @binding(0) var<uniform> material: Material;
@group(1) @binding(1) var surface_sampler: sampler;
@group(1) @binding(2) var diffuse_map: texture_2d<f32>;
@group(1) @binding(3) var normal_map: texture_2d<f32>;
@group(1) @binding(4) var specular_map: texture_2d<f32>;
@group(1) @binding(5) var tint_palette: texture_2d<f32>;
@group(1) @binding(6) var palette_sampler: sampler;
@group(1) @binding(7) var dirt_map: texture_2d<f32>;
@group(1) @binding(8) var diffuse_ramp: texture_2d<f32>;

struct SurfaceInput {
    @location(0) position: vec3f,
    @location(1) normal: vec3f,
    @location(2) tangent: vec4f,
    @location(3) uv0: vec2f,
    @location(4) uv1: vec2f,
    @location(5) colour0: vec4f,
    @location(6) colour1: vec4f,
};

struct SurfaceOutput {
    @builtin(position) position: vec4f,
    // Relative to the scene's origin.
    @location(0) place: vec3f,
    @location(1) normal: vec3f,
    @location(2) tangent: vec3f,
    @location(3) binormal: vec3f,
    @location(4) uv0: vec2f,
    @location(5) colour0: vec4f,
    // The tint palette's colour, white for untinted shaders.
    @location(6) tint: vec3f,
    // The second UV set: a vehicle's dirt is tiled over it.
    @location(7) uv1: vec2f,
};

@vertex
fn vs_surface(input: SurfaceInput) -> SurfaceOutput {
    var output: SurfaceOutput;
    output.position = scene.view_projection * vec4f(input.position, 1.0);
    output.place = input.position;
    // As VS_Transform: the normal normalised, the tangent as it is, the binormal cross(T, N) by the handedness.
    output.normal = normalize(input.normal);
    output.tangent = input.tangent.xyz;
    output.binormal = cross(input.tangent.xyz, output.normal) * input.tangent.w;
    output.uv0 = input.uv0;
    output.uv1 = input.uv1;
    output.colour0 = input.colour0;
    // As VS_TransformD in the _tnt shaders: the palette read at the vertex blue's column, the selector's row.
    output.tint = vec3f(1.0);
    if (material.extra.y > 0.5) {
        output.tint = textureSampleLevel(tint_palette, palette_sampler, vec2f(input.colour0.b, material.extra.z), 0.0).rgb;
    }
    return output;
}

// Schlick's approximation as GTA writes it: the fresnel constant is how much of the light it lets through, so F0
// is one less it.
fn fresnel(fresnel_value: f32, cosine: f32) -> f32 {
    let m = 1.0 - cosine;
    let m2 = m * m;
    return (1.0 - fresnel_value) + fresnel_value * m * m2 * m2;
}

// What a family's deferred pixel shader writes, before lighting: albedo as stored (gamma 2), normal, specular
// intensity, power and fresnel, the natural and artificial ambient scales, and alpha.
struct Surface {
    base: vec3f,
    normal: vec3f,
    intensity: f32,
    power: f32,
    fresnel: f32,
    natural: f32,
    artificial: f32,
    alpha: f32,
};

@fragment
fn fs_surface(input: SurfaceOutput, @builtin(front_facing) front: bool) -> @location(0) vec4f {
    // Every map is sampled before anything is decided, as derivatives need uniform control flow.
    let diffuse = textureSample(diffuse_map, surface_sampler, input.uv0);
    let bump = textureSample(normal_map, surface_sampler, input.uv0);
    let specular_sample = textureSample(specular_map, surface_sampler, input.uv0);
    // A vehicle's dirt tiles closer as dirtLevelMod.z goes down.
    let dirt = textureSample(dirt_map, surface_sampler, input.uv1 * (2.0 - material.dirt_level.z));

    let mode = u32(material.options.z + 0.5);
    if (mode == 1u && diffuse.a < material.extra.x) {
        discard;
    }

    // The surface normal: the bump map's XY scaled by bumpiness (at least 0.001) in the tangent frame, Z rebuilt.
    var normal = normalize(input.normal);
    if (material.options.x > 0.5) {
        let xy = bump.xy * 2.0 - 1.0;
        let z = sqrt(abs(1.0 - dot(xy, xy)));
        let scale = max(0.001, material.specular.w);
        normal = normalize(scale * xy.x * input.tangent + scale * xy.y * input.binormal + z * input.normal);
    }
    // The back of a surface drawn from both sides faces the other way.
    normal = select(-normal, normal, front);

    let view = normalize(scene.eye.xyz - (input.place + scene.origin.xyz));
    let to_light = normalize(light.sun_direction.xyz);
    var surface: Surface;
    if (material.extra.w > 0.5) {
        surface = vehicle_surface(input, diffuse, specular_sample, dirt, normal, view, to_light);
    } else {
        surface = textured_surface(input, diffuse, specular_sample, normal);
    }
    surface.alpha = select(1.0, surface.alpha, mode == 2u);
    let shadow = sun_shadow(input.place, normalize(input.normal));
    return vec4f(lit(input, surface, diffuse, view, to_light, shadow), surface.alpha);
}

// PS_DeferredTextured. Specular intensity, power and fresnel as written to the G-buffer: a specular map's R and G
// squared and B as it is, weighed by specMapIntMask, and its alpha scaling the falloff.
fn textured_surface(input: SurfaceOutput, diffuse: vec4f, specular_sample: vec4f, normal: vec3f) -> Surface {
    var surface: Surface;
    surface.normal = normal;
    surface.intensity = material.specular.z;
    surface.power = material.specular.y;
    if (material.options.y > 0.5) {
        let weighed = vec3f(specular_sample.r * specular_sample.r, specular_sample.g * specular_sample.g, specular_sample.b);
        surface.intensity = material.specular.z * dot(weighed, material.mask.xyz);
        surface.power = material.specular.y * specular_sample.a;
    }
    surface.fresnel = material.specular.x;
    // Tinted shaders multiply the diffuse map by the palette's colour.
    surface.base = diffuse.rgb * input.tint;
    // The vertex colour's red is the natural ambient, its green the artificial.
    surface.natural = input.colour0.r;
    surface.artificial = input.colour0.g;
    surface.alpha = diffuse.a;
    return surface;
}

// PS_DeferredVehicleTextured, as vehicle_paint1 and vehicle_mesh compile it; the rest of the family is one of the two
// with fewer inputs. The vertex colour's red is ambient occlusion, for every term; its blue sets where the dirt level
// applies (the pixel shader's dirt and ramp terms); its green scales the damage deformation in the vertex shader
// (VS_VehicleTransformD), which an undamaged vehicle doesn't have.
fn vehicle_surface(input: SurfaceOutput, diffuse: vec4f, specular_sample: vec4f, dirt: vec4f, normal: vec3f, view: vec3f,
    to_light: vec3f) -> Surface {
    var surface: Surface;
    surface.normal = normal;
    let occlusion = input.colour0.r;
    let level = material.dirt_level;
    let n_dot_v = saturate(dot(normal, view));

    // The paint: its colour, or with a ramp the colour at this viewing angle, pulled towards the ramp's end by the dirt
    // level where the vertex blue lets it.
    var paint = material.paint.rgb;
    if (material.vehicle.z > 0.5 && material.paint.w > 0.0) {
        paint = textureSampleLevel(diffuse_ramp, surface_sampler, vec2f(clamp(n_dot_v, 0.002, 0.998), 0.0), 0.0).rgb;
        if (1.0 - level.z > 0.0) {
            let end = textureSampleLevel(diffuse_ramp, surface_sampler, vec2f(0.998, 0.0), 0.0).x;
            paint = mix(paint, vec3f(end), level.x * input.colour0.b * (1.0 - level.z));
        }
    }
    var base = diffuse.rgb * occlusion * paint;

    // Dirt: how much of it shows, by the dirt map, the level and the vertex blue; its colour over the paint, and the
    // map's own over that where it is dirtiest. Its green roughens the surface, taking the specular away.
    var roughness = 0.0;
    if (material.dirt_colour.w > 0.5) {
        let masked = (1.0 - level.z) * input.colour0.b;
        let spread = (1.0 - level.z) * (masked - 1.0) + 1.0;
        let amount = dirt.x * level.x * spread;
        roughness = dirt.y * level.x * spread;
        base = mix(base, level.y * material.dirt_colour.rgb, amount);
        base = mix(base, vec3f(dirt.z), masked * level.x);
    }

    // Specular: paints from their constants, the specular map only shaping the second lobe; the rest from the map,
    // R² the intensity, G² the power and B the fresnel.
    let rr = specular_sample.r * specular_sample.r;
    let gg = specular_sample.g * specular_sample.g;
    var lobe_strength: f32;
    var lobe_exponent: f32;
    if (material.vehicle.w > 0.5) {
        let weighed = dot(vec3f(rr, gg, specular_sample.b), material.mask.xyz);
        surface.intensity = occlusion * material.specular.z * (1.0 - roughness);
        if (material.vehicle.x < 0.5 && material.options.y > 0.5) {
            surface.intensity *= weighed;
        }
        surface.power = material.specular.y;
        surface.fresnel = material.specular.x;
        lobe_strength = occlusion * 0.75 * level.w * weighed;
        lobe_exponent = specular_sample.a * 15.0;
    } else {
        surface.intensity = 0.6 * occlusion * rr * (1.0 - roughness);
        surface.power = 460.0 * gg;
        surface.fresnel = 1.0 - specular_sample.b;
        lobe_strength = 0.6 * occlusion * rr * level.w;
        lobe_exponent = gg * 20.0;
    }

    // The second lobe, a paint's sheen: lit from the sun swung towards overhead by DirLerp, and kept in the albedo.
    if (material.vehicle.y > 0.5) {
        let towards = normalize(mix(to_light, vec3f(0.0, 0.0, 1.0), material.specular2.w));
        let half_vector = normalize(view + towards);
        let sheen = pow(saturate(dot(normal, half_vector) + 1e-8), lobe_exponent + 1e-8);
        base = min(base + material.specular2.rgb * lobe_strength * sheen, vec3f(240.0));
    }

    surface.base = base;
    surface.natural = occlusion;
    surface.artificial = occlusion;
    surface.alpha = diffuse.a * input.colour0.a;
    return surface;
}

// The game's directional and ambient passes over what a family wrote. The game's lighting has no 1/pi, so the sun
// enters it as its illuminance over pi, and the sky as its irradiance over pi, as the harmonics hold it.
fn lit(input: SurfaceOutput, surface: Surface, diffuse: vec4f, view: vec3f, to_light: vec3f, shadow: f32) -> vec3f {
    let normal = surface.normal;
    // The lighting passes read albedo squared: the maps are gamma 2.
    let albedo = surface.base * surface.base;
    let spec = saturate(surface.intensity);
    // GTA's exponent from the stored power: three times it up to 500, steeply beyond.
    let p = max(surface.power, 0.0);
    let exponent = 3.0 * min(p, 500.0) + 558.0 * max(0.0, p - 500.0);

    let half_vector = normalize(to_light + view);
    let n_dot_l = saturate(dot(normal, to_light));
    let n_dot_v = saturate(dot(normal, view));
    let h_dot_l = saturate(dot(half_vector, to_light));
    let n_dot_h = saturate(dot(normal, half_vector) + 1e-8);

    // Diffuse loses what the surface reflects at this angle; specular is normalised Blinn-Phong.
    let kept = 1.0 - fresnel(surface.fresnel, n_dot_v) * spec;
    let highlight = (exponent + 2.0) / 8.0 * pow(n_dot_h, exponent + 1e-8) * fresnel(surface.fresnel, h_dot_l);
    let sun = light.sun_illuminance.rgb / PI * shadow * (albedo * kept * n_dot_l + vec3f(n_dot_l * spec * highlight));

    // Ambient: the sky's irradiance for this normal, scaled by the natural ambient (squared as the ambient pass
    // decodes it). Outdoors in daylight the game's artificial ambient is nothing to speak of, so it is left out.
    let natural = surface.natural * surface.natural;
    let ambient = albedo * kept * sky_ambient(normal) * natural;

    // Emissive: the vertex colour's blue by the diffuse alpha and emissiveMultiplier, stored as a sixteenth and
    // brought back, facing the camera.
    var glow = vec3f(0.0);
    if (material.options.w > 0.5) {
        let stored = saturate(input.colour0.b / 16.0 * diffuse.a * material.mask.w);
        glow = albedo * stored * 16.0 * n_dot_v;
    }
    return sun + ambient + glow;
}

const PI: f32 = 3.14159265359;
