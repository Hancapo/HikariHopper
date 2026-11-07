// Drawables, shaded as GTA V shades them: the material is rebuilt from the game's deferred pixel shader
// (PS_DeferredTextured, the family of default, normal, spec, normal_spec, emissive, decal and the rest), and lit
// with its directional pass (PS_directional_just_dir) and the core of its ambient one (PS_directional_ambient).
// The game writes the material to a G-buffer and lights it later; here both happen in one pass, with the same
// arithmetic, for one sun and an ambient that doesn't change.
// Scene, the colour helpers and the dither come from scene.wgsl.

struct Material {
    // specularFresnel, specularFalloffMult, specularIntensityMult, bumpiness.
    specular: vec4f,
    // specMapIntMask, emissiveMultiplier.
    mask: vec4f,
    // Has a normal map, reads a specular map, alpha mode (0 opaque, 1 mask, 2 blend), is emissive.
    options: vec4f,
    // Alpha cutoff, has a tint palette, tintPaletteSelector.
    extra: vec4f,
};

@group(1) @binding(0) var<uniform> material: Material;
@group(1) @binding(1) var surface_sampler: sampler;
@group(1) @binding(2) var diffuse_map: texture_2d<f32>;
@group(1) @binding(3) var normal_map: texture_2d<f32>;
@group(1) @binding(4) var specular_map: texture_2d<f32>;
@group(1) @binding(5) var tint_palette: texture_2d<f32>;
@group(1) @binding(6) var palette_sampler: sampler;

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

@fragment
fn fs_surface(input: SurfaceOutput, @builtin(front_facing) front: bool) -> @location(0) vec4f {
    // Every map is sampled before anything is decided, as derivatives need uniform control flow.
    let diffuse = textureSample(diffuse_map, surface_sampler, input.uv0);
    let bump = textureSample(normal_map, surface_sampler, input.uv0);
    let specular_sample = textureSample(specular_map, surface_sampler, input.uv0);

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

    // Specular intensity, power and fresnel, as written to the G-buffer: a specular map's R and G squared and B as it
    // is, weighed by specMapIntMask, and its alpha scaling the falloff.
    var intensity = material.specular.z;
    var power = material.specular.y;
    if (material.options.y > 0.5) {
        let weighed = vec3f(specular_sample.r * specular_sample.r, specular_sample.g * specular_sample.g, specular_sample.b);
        intensity = material.specular.z * dot(weighed, material.mask.xyz);
        power = material.specular.y * specular_sample.a;
    }
    let fresnel_value = material.specular.x;

    // Tinted shaders multiply the diffuse map by the palette's colour; the lighting passes then read albedo squared,
    // as the maps are gamma 2.
    let base = diffuse.rgb * input.tint;
    let albedo = base * base;
    let spec = saturate(intensity);
    // GTA's exponent from the stored power: three times it up to 500, steeply beyond.
    let p = max(power, 0.0);
    let exponent = 3.0 * min(p, 500.0) + 558.0 * max(0.0, p - 500.0);

    let view = normalize(scene.eye.xyz - (input.place + scene.origin.xyz));
    let light = normalize(scene.sun.xyz);
    let half_vector = normalize(light + view);
    let n_dot_l = saturate(dot(normal, light));
    let n_dot_v = saturate(dot(normal, view));
    let h_dot_l = saturate(dot(half_vector, light));
    let n_dot_h = saturate(dot(normal, half_vector) + 1e-8);

    // Diffuse loses what the surface reflects at this angle; specular is normalised Blinn-Phong.
    let kept = 1.0 - fresnel(fresnel_value, n_dot_v) * spec;
    let highlight = (exponent + 2.0) / 8.0 * pow(n_dot_h, exponent + 1e-8) * fresnel(fresnel_value, h_dot_l);
    let sun = scene.sun_colour.rgb * (albedo * kept * n_dot_l + vec3f(n_dot_l * spec * highlight));

    // Ambient: a hemisphere by how far the normal faces up, scaled by the vertex colour's ambient channels (red
    // natural, green artificial), each squared as the ambient pass decodes them.
    let up = saturate(normal.z * 0.5 + 0.5);
    let natural = input.colour0.r * input.colour0.r;
    let artificial = input.colour0.g * input.colour0.g;
    let sky = (scene.ambient_down.rgb + scene.ambient_up.rgb * up) * natural + scene.artificial.rgb * artificial;
    let ambient = albedo * kept * sky;

    // Emissive: the vertex colour's blue by the diffuse alpha and emissiveMultiplier, stored as a sixteenth and
    // brought back, facing the camera.
    var glow = vec3f(0.0);
    if (material.options.w > 0.5) {
        let stored = saturate(input.colour0.b / 16.0 * diffuse.a * material.mask.w);
        glow = albedo * stored * 16.0 * n_dot_v;
    }

    let colour = (sun + ambient + glow) * scene.sun.w;
    let alpha = select(1.0, diffuse.a, mode == 2u);
    return vec4f(dither(colour, input.position.xy), alpha);
}
