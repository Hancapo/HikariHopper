// GTA V's fog, as the game works it out (checked against Enhanced's shaders): the height fog along a ray
// from the camera, the haze past it, and their colour, towards the sun and the
// moon, of the atmosphere and of the ground. The sky takes it without the haze; the world with it:
// its opaque surfaces by the world's fog pass (gta_world_fog_pass.wgsl, as the game's depth effects
// pass), its water and blended surfaces in their own shaders (gta_world_fog.wgsl). The constants are
// the game's (GtaSky.Fog).

struct GtaFog {
    // globalFogParams: the fog's start and end; the haze's density (negated), alpha and the horizon tint's scale over
    // the fog's end, the ground fog's density at the camera (negated); the haze's start, the ground fog's alpha and its
    // height falloff, the haze at the far clip; where the sun is and the power its glow in the fog falls off by; the
    // moon's.
    params: array<vec4f, 5>,
    // The fog's colours: towards the sun, the atmosphere, the ground (with the haze's red, green and blue in w),
    // towards the moon.
    sun: vec4f,
    atmosphere: vec4f,
    ground: vec4f,
    moon: vec4f,
};

// The height fog along a ray from the camera, and how far into the fog it goes.
fn gta_ground_fog(fog: GtaFog, ray: vec3f) -> vec2f {
    let full = length(ray);
    let dist = max(0.0, full - fog.params[0].x);
    let delta_z = ray.z * (dist / max(full, 1e-6));
    let t = fog.params[2].z * delta_z;
    let along = select(1.0, (1.0 - exp(-t)) / t, abs(delta_z) > 0.01);
    let value = min(1.0, fog.params[1].w * dist * along);
    return vec2f(1.0 - saturate(exp(value)), dist);
}

// The fog's colour along a ray from the camera, and how much of it there is; the haze by its scale.
fn gta_fog(fog: GtaFog, ray: vec3f, haze_scale: f32) -> vec4f {
    let ground = gta_ground_fog(fog, ray);
    let amount = ground.x * fog.params[2].y;
    let d = normalize(ray);
    let moon_amount = pow(saturate(dot(d, fog.params[4].xyz)), fog.params[4].w);
    let sun_amount = pow(saturate(dot(d, fog.params[3].xyz)), fog.params[3].w);
    // The haze where the ground fog isn't, so they don't double up.
    let haze_blend = haze_scale * fog.params[1].y * (1.0 - amount);
    let haze = haze_blend * (1.0 - exp(fog.params[1].x * max(0.0, ground.y - fog.params[2].x)));
    let atmosphere_blend = 1.0 - exp(-fog.params[1].z * ground.y);
    let atmosphere = mix(mix(fog.atmosphere.rgb, fog.moon.rgb, moon_amount), fog.sun.rgb, sun_amount);
    let haze_colour = vec3f(fog.sun.w, fog.atmosphere.w, fog.ground.w);
    let colour = mix(mix(fog.ground.rgb, atmosphere, atmosphere_blend), haze_colour, haze_blend);
    return vec4f(colour, saturate(haze + amount));
}

// The fog without its haze, as the sky takes it.
fn gta_fog_no_haze(fog: GtaFog, ray: vec3f) -> vec4f {
    return gta_fog(fog, ray, 0.0);
}
