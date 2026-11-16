// The sun's shadow of Red Dead Redemption 2's world (after RedFury's vs_world_shadow): each streamed asset's casters
// drawn once per placement into a cascade, their depth alone, cut-outs where RedFury's simple alpha test
// (rdr2_material.wgsl's simple_alpha_tested_out) keeps them. Standalone: the cascade's view, the start of RDR2's material
// group, and the asset's placements as rdr2_world.wgsl reads them.

struct ShadowView {
    // From the world to the cascade's clip space: the cascade's view, after the move to the camera it was fitted for.
    matrix: mat4x4f,
    // That camera, where a plant's fade is measured from (as rdr2_world.wgsl's grass_fade).
    origin: vec4f,
};

@group(0) @binding(0) var<uniform> shadow_view: ShadowView;

// The start of rdr2_material.wgsl's MaterialUniforms: base_colour_factor, surface, emission_alpha_layers, decal_opacity,
// flags.
struct Rdr2ShadowMaterial {
    base_colour_factor: vec4f,
    surface: vec4f,
    emission_alpha_layers: vec4f,
    decal_opacity: vec4f,
    flags: vec4u,
};

@group(1) @binding(0) var<uniform> material: Rdr2ShadowMaterial;
@group(1) @binding(1) var material_sampler: sampler;
@group(1) @binding(2) var base_texture: texture_2d<f32>;

struct WorldInstance {
    m0: vec4f, m1: vec4f, m2: vec4f, m3: vec4f,
    n0: vec4f, n1: vec4f, n2: vec4f, n3: vec4f,
};

@group(3) @binding(0) var<storage, read> world_instances: array<WorldInstance>;

fn caster_position(position: vec3f, source_index: u32) -> vec4f {
    let instance = world_instances[source_index];
    let model = mat4x4f(instance.m0, instance.m1, instance.m2, instance.m3);
    var fade = 1.0;
    if (instance.n3.z > 0.5) {
        let distance = length(instance.m3.xyz - shadow_view.origin.xyz);
        fade = 1.0 - smoothstep(instance.n3.x, max(instance.n3.x + 0.01, instance.n3.y), distance);
    }
    return shadow_view.matrix * (model * vec4f(position * fade, 1.0));
}

// Solid casters: depth alone, no fragment stage.
@vertex
fn vs_world_shadow(@location(0) position: vec3f, @location(8) source_index: u32) -> @builtin(position) vec4f {
    return caster_position(position, source_index);
}

struct MaskedOutput {
    @builtin(position) position: vec4f,
    @location(0) uv: vec2f,
    @location(1) alpha: f32,
};

@vertex
fn vs_world_shadow_masked(@location(0) position: vec3f, @location(3) uv: vec2f, @location(5) colour: vec4f,
    @location(8) source_index: u32) -> MaskedOutput {
    var output: MaskedOutput;
    output.position = caster_position(position, source_index);
    output.uv = uv;
    output.alpha = colour.a;
    return output;
}

@fragment
fn fs_world_shadow_masked(input: MaskedOutput) {
    var alpha = textureSample(base_texture, material_sampler, input.uv).a * material.base_colour_factor.a;
    if (((material.flags.w >> 3u) & 3u) == 2u) {
        alpha *= input.alpha;
    }
    if (alpha < material.emission_alpha_layers.y) {
        discard;
    }
}
