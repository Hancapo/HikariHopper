// What Red Dead Redemption 2's material (rdr2_material.wgsl, RedFury's viewer_material.wgsl) reads of its light, from
// the viewer's (light.wgsl), in RedFury's units. RedFury's diffuse is divided by pi and lit by the sun's illuminance; the
// viewer's light, GTA's, has no pi in its lighting, so the sun's illuminance is its colour times pi. The ambient is
// irradiance over pi in both, and the reflection map holds radiance, as RedFury's sky does. Put after light.wgsl.

// The camera, relative to the scene's origin, where the material's positions are.
fn rdr2_eye() -> vec3f {
    return scene.eye.xyz - scene.origin.xyz;
}

// The key light's illuminance, as RedFury's sun_colour_intensity.xyz * w.
fn rdr2_sun() -> vec3f {
    return light.sun_colour.rgb * 3.14159265359;
}

// Irradiance over pi for a unit normal: RedFury's sky harmonics, the viewer's ambient hemisphere.
fn sky_irradiance(n: vec3f) -> vec3f {
    return gta_ambient(n);
}

// The sky along a reflection: RedFury's sky-view LUT, here the reflection map (sky and the drawable itself), at a mip as
// rough as the surface.
fn rdr2_sky(direction: vec3f, roughness: f32) -> vec3f {
    let level = clamp(roughness, 0.0, 1.0) * max(light.reflection.x - 1.0, 0.0);
    return textureSampleLevel(reflection_map, reflection_sampler, paraboloid_uv(direction), level).rgb;
}

// No atmosphere between the camera and a drawable in the viewer.
fn apply_aerial_perspective(colour: vec3f, world_position: vec3f, pixel: vec2f) -> vec3f {
    return colour;
}
