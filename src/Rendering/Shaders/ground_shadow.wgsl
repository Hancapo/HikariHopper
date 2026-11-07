// The sun's shadow of a drawable on the backdrop's ground: one full-screen triangle after the backdrop, finding the
// ground under each pixel as backdrop.wgsl does and darkening it by the shadow map there (light.wgsl). Blended as a
// multiply, so the grid and the void stay the theme's colours where nothing shades them.
// Scene comes from scene.wgsl, the light from light.wgsl.

struct GroundShadow {
    @builtin(position) position: vec4f,
    @location(0) ndc: vec2f,
};

// How dark the shadow falls on the ground: enough to read, not enough to hide the grid in it.
const GROUND_SHADOW_STRENGTH: f32 = 0.55;

@vertex
fn vs_ground_shadow(@builtin(vertex_index) index: u32) -> GroundShadow {
    var corners = array<vec2f, 3>(vec2f(-1.0, -1.0), vec2f(3.0, -1.0), vec2f(-1.0, 3.0));
    var output: GroundShadow;
    output.position = vec4f(corners[index], 0.0, 1.0);
    output.ndc = corners[index];
    return output;
}

@fragment
fn fs_ground_shadow(input: GroundShadow) -> @location(0) vec4f {
    let aspect = scene.projection.x;
    let tan_half = scene.projection.y;
    let ray = normalize(scene.forward.xyz + scene.right.xyz * input.ndc.x * aspect * tan_half + scene.up.xyz * input.ndc.y * tan_half);
    let toward = select(ray.z, -1e-6, abs(ray.z) < 1e-6);
    let distance = (scene.projection.z - scene.eye.z) / toward;
    if (distance <= 0.0) {
        return vec4f(1.0);
    }
    let point = scene.eye.xyz + ray * distance - scene.origin.xyz;
    let lit = sun_shadow(point, vec3f(0.0, 0.0, 1.0));
    // The sun below the horizon casts nothing; at grazing angles the ground's shadow fades out, as the grid does.
    let fade = smoothstep(0.0, 0.08, abs(ray.z)) * smoothstep(0.0, 0.1, light.sun_direction.z);
    return vec4f(vec3f(1.0 - (1.0 - lit) * GROUND_SHADOW_STRENGTH * fade), 1.0);
}
