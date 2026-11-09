// The key light's shadow map: drawables' depth seen from the sun, through an orthographic view fitted to them.
// Standalone (no scene.wgsl): its inputs are the view below and the vertices' positions; cut-out (masked) surfaces
// also read their diffuse map, so only what the main pass keeps casts a shadow, as the game's shadow pass clips them.

struct ShadowView {
    // From origin-relative positions to the shadow map's clip space.
    matrix: mat4x4f,
};

@group(0) @binding(0) var<uniform> shadow_view: ShadowView;

@vertex
fn vs_shadow(@location(0) position: vec3f) -> @builtin(position) vec4f {
    return shadow_view.matrix * vec4f(position, 1.0);
}

// The start of surface.wgsl's Material: the alpha cutoff is extra.x.
struct ShadowMaterial {
    specular: vec4f,
    mask: vec4f,
    options: vec4f,
    extra: vec4f,
};

@group(1) @binding(0) var<uniform> material: ShadowMaterial;
@group(1) @binding(1) var surface_sampler: sampler;
@group(1) @binding(2) var diffuse_map: texture_2d<f32>;

struct MaskedOutput {
    @builtin(position) position: vec4f,
    @location(0) uv: vec2f,
};

@vertex
fn vs_shadow_masked(@location(0) position: vec3f, @location(3) uv: vec2f) -> MaskedOutput {
    var output: MaskedOutput;
    output.position = shadow_view.matrix * vec4f(position, 1.0);
    output.uv = uv;
    return output;
}

// A cut-out surface casts only where its diffuse alpha passes the cutoff, as in the main pass; hair, solid in its first
// pass only where it is all but opaque, casts where it covers at least half.
@fragment
fn fs_shadow_masked(input: MaskedOutput) {
    if (textureSample(diffuse_map, surface_sampler, input.uv).a < min(material.extra.x, 128.5 / 255.0)) {
        discard;
    }
}
