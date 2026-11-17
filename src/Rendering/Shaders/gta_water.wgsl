// GTA V's water, as water.fxc's PS_WaterSinglePass shades it (the same arithmetic in Enhanced's program as in Legacy's,
// whose constants are named): the ocean, lakes and pools of water.xml, and the water shaders' drawables (rivers,
// fountains), each placement drawn as the world draws its assets (world_instances.wgsl).
//   - Light: the water's ambient colour, and its directional colour by the sun's height and the shadow, through the fog
//     colour (squared, as the map is stored): the colour the water scatters.
//   - Bump: graphics.ytd's wave bump, tiled, each tile offset by the noise map to hide the tiling, then two static
//     bumps drifting with time, which bend the normal by the ripple scale.
//   - The scene behind (the copy, light.wgsl's water_background and water_depth): seen through the water's depth,
//     exp(-78.4 a^2 d) and, along the view, exp(-235.3 a^2 d |v.z|), with a the water's alpha; sunlight scattered back
//     through the refracted view.
//   - Foam over the shallows, then the reflection map (the same paraboloid) by a fresnel on N.V, and the sun's highlight
//     by the specular falloff, all faded in over the first metre of depth.
//   - Then the world's fog along the view to the surface: the game draws its water before its fog pass, which fogs the
//     water by its depth; here the fog pass goes first and the water, which covers what's behind it, fogs itself.
// The game's ambient and directional water colours are taken as the timecycle's natural ambient and sun colour; its
// BlendSampler (the fresnel and foam curves) as Schlick's fresnel and a steep foam ramp; its wet map isn't read.
// Put after scene.wgsl, light.wgsl, water_common.wgsl, world_instances.wgsl and gta_fog.wgsl.

struct Water {
    // FogMin and FogMax: the fog map's corners on the map, for the ocean.
    fog_region: vec4f,
    // RippleScale, SpecularIntensity, SpecularFalloff and OceanFoamScale.
    scales: vec4f,
    // Whether the fog map is read by world position, the wave bump's tiling, the static bump's strength, and the sun
    // scattered back out (GtaWater).
    options: vec4f,
    extra: vec4f,
};

@group(1) @binding(0) var<uniform> water: Water;
@group(1) @binding(1) var water_sampler: sampler;
@group(1) @binding(2) var fog_map: texture_2d<f32>;
@group(1) @binding(3) var static_bump: texture_2d<f32>;
@group(1) @binding(4) var wave_bump: texture_2d<f32>;
@group(1) @binding(5) var noise_map: texture_2d<f32>;
@group(1) @binding(6) var foam_map: texture_2d<f32>;

struct WaterInput {
    @location(0) position: vec3f,
    @location(1) normal: vec3f,
    @location(2) tangent: vec4f,
    @location(3) uv0: vec2f,
    @location(4) uv1: vec2f,
    @location(5) colour0: vec4f,
    @location(6) colour1: vec4f,
    @location(7) uv2: vec2f,
};

struct WaterOutput {
    @builtin(position) position: vec4f,
    // Relative to the scene's origin.
    @location(0) place: vec3f,
    @location(1) normal: vec3f,
    @location(2) uv: vec2f,
    // The water's alpha (water.xml's corners'), which sets how fast it fogs.
    @location(3) alpha: f32,
};

@vertex
fn vs_gta_water(input: WaterInput, @location(8) source_index: u32) -> WaterOutput {
    let instance = world_instances[source_index];
    let model = instance_model(instance);
    var output: WaterOutput;
    output.place = (model * vec4f(input.position, 1.0)).xyz - scene.origin.xyz;
    output.normal = normalize((instance_normal_matrix(instance) * vec4f(input.normal, 0.0)).xyz);
    output.uv = input.uv0;
    output.alpha = input.colour0.a;
    output.position = scene.view_projection * vec4f(output.place, 1.0);
    return output;
}

// BlendSampler's red: the fresnel the game reads at (1 - N.V) * 0.3 + N.V, Schlick's for water here.
fn water_fresnel(ndotv: f32) -> f32 {
    let c = saturate(ndotv);
    return 0.02 + 0.98 * pow(1.0 - c, 5.0);
}

// BlendSampler's green: the foam the game keeps of a value; a steep ramp over the strongest foam here (the game makes
// its curve on the CPU).
fn water_foam(value: f32) -> f32 {
    return smoothstep(0.17, 0.25, value);
}

@fragment
fn fs_gta_water(input: WaterOutput, @builtin(front_facing) front: bool) -> @location(0) vec4f {
    let world = input.place + scene.origin.xyz;
    let from_eye = input.place - (scene.eye.xyz - scene.origin.xyz);
    let view = normalize(from_eye);
    let depth = dot(from_eye, scene.forward.xyz);
    let sun = normalize(light.sun_direction.xyz);
    let ambient = light.natural_down.rgb;
    let directional = light.sun_colour.rgb;
    let geometric = input.normal * select(-1.0, 1.0, front);

    // The sun's shadow, squared as the game takes it.
    let shadowed = sun_shadow(input.place, geometric);
    let shadow = min(shadowed * shadowed, 1.0);
    let lighting = ambient + directional * max(sun.z, 0.0) * shadow;

    var fog_uv = input.uv;
    if (water.options.x > 0.5) {
        fog_uv = vec2f((world.x - water.fog_region.x) / (water.fog_region.z - water.fog_region.x),
            (water.fog_region.w - world.y) / (water.fog_region.w - water.fog_region.y));
    }
    let fog_sample = textureSample(fog_map, water_sampler, fog_uv).rgb;
    let fog = fog_sample * fog_sample;
    var colour = lighting * fog;

    // The bump: the wave bump's tiles, offset by the noise map, blended across their edges; then the static bumps.
    let wave_uv = world.xy * water.options.y;
    let tile = floor(wave_uv) / 256.0;
    let noise = textureSampleLevel(noise_map, water_sampler, tile, 0.0);
    let shifted = textureSample(wave_bump, water_sampler, noise.wy * 100.0 + wave_uv).xy * 2.0 - 1.0;
    let plain = textureSample(wave_bump, water_sampler, wave_uv).xy * 2.0 - 1.0;
    let cell = abs(0.5 - fract(wave_uv)) * 2.0;
    var bump = mix(shifted, plain, pow(max(cell.x, cell.y), 32.0));
    let drift = light.water.x * 0.1;
    let static0 = textureSample(static_bump, water_sampler, world.xy * 0.001953 - drift).wy * 2.0 - 1.0;
    let static1 = textureSample(static_bump, water_sampler, world.yx * 0.002232 + drift).yw * 2.0 - 1.0;
    bump += (-static0 - static1) * water.options.z;
    let n = normalize(vec3f(bump * water.scales.x + geometric.xy, geometric.z));
    let foam_factor = length(bump) * 0.27 + 0.44;

    // The view refracted into the water (eta 2/3), half-way between the bumped and the plain normal.
    let refract_normal = normalize((n + geometric) * 0.5);
    let cosine = dot(view, refract_normal);
    let k = 1.0 - (1.0 - cosine * cosine) * 0.444444;
    var refracted = vec3f(0.0);
    if (k >= 0.0) {
        refracted = view * 0.666667 - refract_normal * (cosine * 0.666667 + sqrt(k));
    }
    var sun_through = saturate(dot(refracted, sun));
    sun_through *= sun_through;

    // The scene behind, and how much water it's seen through.
    let pixel = vec2i(input.position.xy);
    let scene_depth = water_depth_at(pixel);
    // Nothing behind: deep water over the sky the view meets (the copy holds no sky, which the tone map adds later).
    let thickness = select(max(water_view_distance(scene_depth) - depth, 0.0), 100.0, scene_depth == 0.0);
    sun_through *= min(thickness * 0.1, 1.0);
    sun_through = sun_through * sun_through * water.options.w;
    colour = colour * light.water.y + fog * directional * sun_through;
    var behind = water_background_at(pixel);
    if (scene_depth == 0.0) {
        behind = textureSampleLevel(reflection_map, reflection_sampler, paraboloid_uv(refracted), 0.0).rgb;
    }
    let fogged = fog * behind * 2.0;
    let a2 = input.alpha * input.alpha;
    let through = exp(-78.433037 * a2 * thickness);
    let along = exp(-235.299103 * a2 * thickness * abs(view.z));
    colour = mix(colour, along * (behind - fogged) + fogged, through);
    let shore = min(thickness, 1.0);

    // Foam over the shallows, lit as the surface is.
    let foam_light = saturate(dot(n, sun) * 0.7 + 0.3) * directional * shadow + ambient;
    let foam = water_foam(textureSample(foam_map, water_sampler, world.xy * water.scales.w).y * 0.35 * foam_factor);
    colour += foam_light * shore * foam;

    // The reflection map, by the normal turned most of the way up, and the fresnel.
    let reflect_normal = normalize(mix(n, vec3f(0.0, 0.0, 1.0), 0.833333));
    let environment = textureSampleLevel(reflection_map, reflection_sampler, paraboloid_uv(reflect(view, reflect_normal)), 0.0).rgb;
    let fresnel = water_fresnel(dot(-view, n));
    var surface = mix(colour, environment, fresnel);

    // The sun's highlight.
    let half_vector = normalize(-view + sun);
    let highlight = pow(saturate(dot(half_vector, n)), water.scales.z) * water.scales.y * shadow;
    surface += directional * highlight;
    // The world's fog at the water's surface, as the game's fog pass takes the water once it's drawn (gta_fog.wgsl).
    return gta_world_fogged(vec4f(mix(colour, surface, shore), 1.0), input.place);
}
