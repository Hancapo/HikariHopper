// What Red Dead Redemption 2's material (Rdr2/material.wgsl, RedFury's viewer_material.wgsl) reads of its light, from
// the viewer's (light.wgsl), in RedFury's units. RedFury's diffuse is divided by pi and lit by the sun's illuminance; the
// viewer's light, GTA's, has no pi in its lighting, so the sun's illuminance is its colour times pi. The ambient is
// irradiance over pi in both, and the reflection map holds radiance, as RedFury's sky does. Put after light.wgsl.

// The camera, relative to the scene's origin, where the material's positions are.
fn rdr2_eye() -> vec3f {
    return scene.eye.xyz - scene.origin.xyz;
}

// Whether the world is lit by RDR2's own sky, in its own units (ViewportRenderer.Rdr2Sky.cs).
fn rdr2_sky_lit() -> bool {
    return light.rdr2_sun.w > 0.5;
}

// The key light's illuminance, as RedFury's sun_colour_intensity.xyz * w: RDR2's sky's, or the viewer's.
fn rdr2_sun() -> vec3f {
    return select(light.sun_colour.rgb * 3.14159265359, light.rdr2_sun.rgb, rdr2_sky_lit());
}

// RDR2's sky's ambient cube for a unit normal, as the game reads its ambient cube: each axis's face it faces, by the
// square of the normal along it.
fn rdr2_ambient_cube(n: vec3f) -> vec3f {
    let cube = light.rdr2_ambient;
    let x = select(cube[0].rgb, cube[1].rgb, n.x < 0.0);
    let y = select(cube[3].rgb, cube[4].rgb, n.y < 0.0);
    let z = select(cube[2].rgb, cube[5].rgb, n.z < 0.0);
    return n.x * n.x * x + n.y * n.y * y + n.z * n.z * z;
}

// Irradiance over pi for a unit normal: RedFury's sky harmonics; RDR2's sky's ambient cube, or the viewer's ambient
// hemisphere.
fn sky_irradiance(n: vec3f) -> vec3f {
    return select(gta_ambient(n), rdr2_ambient_cube(n), rdr2_sky_lit());
}

// The sky along a reflection: RedFury's sky-view LUT; with RDR2's sky, its ambient cube that way (the sky isn't drawn
// into a reflection map yet), else the reflection map (sky and the drawable itself), at a mip as rough as the surface.
fn rdr2_sky(direction: vec3f, roughness: f32) -> vec3f {
    let level = clamp(roughness, 0.0, 1.0) * max(light.reflection.x - 1.0, 0.0);
    let mapped = textureSampleLevel(reflection_map, reflection_sampler, paraboloid_uv(direction), level).rgb;
    return select(mapped, rdr2_ambient_cube(normalize(direction)), rdr2_sky_lit());
}

// No atmosphere between the camera and a drawable in the viewer.
fn apply_aerial_perspective(colour: vec3f, world_position: vec3f, pixel: vec2f) -> vec3f {
    return colour;
}
