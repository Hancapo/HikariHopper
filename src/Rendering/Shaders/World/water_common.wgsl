// What every game's water reads of the scene behind it (Scene/light.wgsl's water_background and water_depth, copied before
// water is drawn): its distance from the camera along the view, and where a pixel of it is. Put after Scene/scene.wgsl and
// Scene/light.wgsl.

// The view's distance to a point at this depth: the depth is the near plane over it (reversed Z, infinitely far).
fn water_view_distance(depth: f32) -> f32 {
    return light.water.z / max(depth, 1e-12);
}

fn water_viewport() -> vec2f {
    return max(scene.viewport.zw, vec2f(1.0));
}

// A pixel's point at a depth, relative to the scene's origin.
fn water_point(pixel: vec2f, depth: f32) -> vec3f {
    let ndc = vec2f(pixel.x / water_viewport().x * 2.0 - 1.0, 1.0 - pixel.y / water_viewport().y * 2.0);
    let tan_half = scene.projection.y;
    let direction = scene.forward.xyz + scene.right.xyz * (ndc.x * tan_half * scene.projection.x) + scene.up.xyz * (ndc.y * tan_half);
    return scene.eye.xyz - scene.origin.xyz + direction * water_view_distance(depth);
}

// The copied depth at a pixel, inside the frame.
fn water_depth_at(pixel: vec2i) -> f32 {
    let limit = vec2i(textureDimensions(water_depth)) - vec2i(1);
    return textureLoad(water_depth, clamp(pixel, vec2i(0), limit), 0);
}

fn water_background_at(pixel: vec2i) -> vec3f {
    let limit = vec2i(textureDimensions(water_background)) - vec2i(1);
    return textureLoad(water_background, clamp(pixel, vec2i(0), limit), 0).rgb;
}
