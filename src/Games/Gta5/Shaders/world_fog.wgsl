// GTA V's fog over its world, as its shaders read it (ViewportRenderer.WorldFog.cs): the fog's constants (Gta5/fog.wgsl)
// and the fog rays', through Scene/light.wgsl's group; and the fog rays' map, how much of each pixel's ray
// through the fog the sun reaches (Gta5/world_fog_pass.wgsl), which darkens the fog's colour where it doesn't. With no
// world's sky, no fog; with no sun's shadow, no rays.
// Put after Scene/scene.wgsl, Scene/light.wgsl and Gta5/fog.wgsl.

struct WorldFog {
    fog: GtaFog,
    // The fog rays: fogray_intensity, then fogray_nearfade and fogray_farfade (over the last cascade's reach).
    rays: vec4f,
};

@group(2) @binding(9) var<uniform> world_fog: WorldFog;
// How much of each ray the sun reaches, and its depth along the view: half the frame's size.
@group(2) @binding(10) var fog_ray_map: texture_2d<f32>;

// globalFogRayIntensity: none without the sun's shadow.
fn gta_fog_ray_strength() -> f32 {
    return select(0.0, world_fog.rays.x, light.shadow_parameters.w > 0.5);
}

// The fog rays' intensity: what the fog's colour is taken by at a pixel, from the map as it is (filtered).
fn gta_fog_ray_intensity(pixel: vec2f) -> f32 {
    let strength = gta_fog_ray_strength();
    if (strength <= 0.0) {
        return 1.0;
    }
    let uv = pixel / (vec2f(textureDimensions(fog_ray_map)) * 2.0);
    let ray = textureSampleLevel(fog_ray_map, reflection_sampler, uv, 0.0).x;
    return saturate(mix(1.0, ray, strength));
}

// The depth effects (with the fog rays in the forward pass): a forward-drawn surface of the world taken through the world's fog, by
// its place relative to the scene's origin (the camera, in the world) and its pixel.
fn gta_world_fogged(colour: vec4f, place: vec3f, pixel: vec2f) -> vec4f {
    var fog = gta_fog(world_fog.fog, place - (scene.eye.xyz - scene.origin.xyz), 1.0);
    fog = vec4f(fog.rgb * gta_fog_ray_intensity(pixel), fog.a);
    return vec4f(mix(colour.rgb, fog.rgb, fog.a), colour.a);
}
