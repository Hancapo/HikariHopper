// The key light's shadow map: drawables' depth seen from the sun, through an orthographic view fitted to them.
// Standalone (no scene.wgsl): its only input is the view below and the vertices' positions.

struct ShadowView {
    // From origin-relative positions to the shadow map's clip space.
    matrix: mat4x4f,
};

@group(0) @binding(0) var<uniform> shadow_view: ShadowView;

@vertex
fn vs_shadow(@location(0) position: vec3f) -> @builtin(position) vec4f {
    return shadow_view.matrix * vec4f(position, 1.0);
}
