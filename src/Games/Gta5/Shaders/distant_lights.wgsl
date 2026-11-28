// GTA V's distant LOD lights: each light of its medium and large distlodlights_* maps a camera-facing sprite of
// graphics.ytd's "distant_light", added over the world after its blended surfaces and before the coronas, tested against
// the scene's depth and writing none, as the im effect's VS_DistantLights3d and PS_DistantLights draw them (checked
// against Enhanced's).
//   - Size: distantlights.size across twice, doubled once the camera is distantlights.sizeDist above sizeDistStart;
//     never drawn smaller than sizeMin times sizeUpscale over 1024 of the half screen's width, its light spread over
//     what it grew by.
//   - Where: from the category's corona range out (lodlight.medium.corona.range, lodlight.large.corona.range), with a
//     hard cut, the coronas being nearer; as far as the game has the light's map streamed in (which lights are drawn).
//   - Light: its colour (as stored, linear) by its intensity byte, by the hour's fade (the street lights' hours or the
//     others'), by distantlights.streetlight.HDRIntensity and sprite_brightness, by what the fog leaves of it (the
//     coronas' fog), and brighter far off, up to sixteen times by 2.5 km; then the texture's red to the fourth power,
//     the flicker and the twinkle (graphics.ytd's "waternoise", per metre and per 512 m patch, slowly moving).
//   - Left out: Enhanced's second layer of occluders (a depth and its opacity), as the coronas leave it out, and the
//     sprites in water reflections.
// Put after Scene/scene.wgsl, Gta5/sky_system.wgsl, Gta5/fog.wgsl and Gta5/sky.wgsl: the fog's constants are the sky's.

struct DistantFrame {
    // The camera's right, then the sprites' size across.
    right: vec4f,
    // The camera's up, then how much a sprite grows by per metre of view depth to stay its smallest.
    up: vec4f,
    // The camera's forward, then the HDR intensity (streetlight.HDRIntensity by sprite_brightness).
    forward: vec4f,
    // The camera in the world, then the noise's time (game milliseconds by twinkleSpeed over 256).
    eye: vec4f,
    // Each category's corona range (medium in y, large in z), then the flicker's depth.
    near: vec4f,
    // The hour's fade as a byte's worth (0 to 255) for the others and for the street lights, then the twinkle's depth
    // (twinkleAmount by sprite_distant_light_twinkle).
    alpha: vec4f,
};

// A light as GtaDistantLights.Light lays it out.
struct DistantLight {
    position: vec3f,
    // 0xIIRRGGBB.
    rgbi: u32,
    // Its category in the low four bits, then whether it is a street light.
    kind: u32,
    spare0: u32,
    spare1: u32,
    spare2: u32,
};

@group(1) @binding(1) var<uniform> distant: DistantFrame;
@group(1) @binding(2) var<storage, read> distant_lights: array<DistantLight>;
@group(1) @binding(3) var distant_map: texture_2d<f32>;
@group(1) @binding(4) var water_noise: texture_2d<f32>;
@group(1) @binding(5) var distant_sampler: sampler;
@group(1) @binding(6) var noise_sampler: sampler;

struct DistantVarying {
    @builtin(position) position: vec4f,
    @location(0) colour: vec3f,
    @location(1) uv: vec2f,
    // The light's place across the ground, where the noise is read.
    @location(2) @interpolate(flat) place: vec2f,
};

@vertex
fn vs_distant_light(@builtin(vertex_index) vertex: u32, @builtin(instance_index) instance: u32) -> DistantVarying {
    var output: DistantVarying;
    let light = distant_lights[instance];
    // The strip's corners: (1, 0), (0, 0), (1, 1), (0, 1).
    let corner = vec2f(select(1.0, 0.0, (vertex & 1u) == 1u), select(0.0, 1.0, vertex >= 2u));
    let ray = light.position - distant.eye.xyz;
    let distance = length(ray);
    // Never smaller than its smallest: grown by the view depth, its light spread over what it grew by.
    let grow = max(1.0, dot(ray, distant.forward.xyz) * distant.up.w);
    let category = light.kind & 15u;
    let near = select(distant.near.y, distant.near.z, category == 2u);
    let shown = select(0.0, 1.0, distance >= near);
    let fade = select(distant.alpha.x, distant.alpha.y, (light.kind & 16u) != 0u);
    // The intensity byte by the hour's, as the game packs it again.
    let intensity = floor(f32(light.rgbi >> 24u) * fade / 255.0);
    let alpha = shown / (grow * grow) * (1.0 - gta_fog_amount(sky.fog, ray));
    let boost = 1.0 + 15.0 * saturate((distance - 500.0) / 2000.0);
    let rgb = vec3f(f32((light.rgbi >> 16u) & 255u), f32((light.rgbi >> 8u) & 255u), f32(light.rgbi & 255u)) / 255.0;
    output.colour = rgb * (intensity / 255.0) * distant.forward.w * alpha * boost;
    // Gone to a point where nothing of it shows.
    let size = select(0.0, distant.right.w * grow, alpha > 0.0 && intensity > 0.0);
    let place = light.position + (distant.right.xyz * (corner.x - 0.5) + distant.up.xyz * (corner.y - 0.5)) * size;
    output.position = scene.view_projection * vec4f(place - scene.origin.xyz, 1.0);
    output.uv = corner;
    output.place = light.position.xy;
    return output;
}

@fragment
fn fs_distant_light(input: DistantVarying) -> @location(0) vec4f {
    let t = textureSample(distant_map, distant_sampler, input.uv).r;
    let t2 = t * t;
    let time = distant.eye.w;
    let flicker = mix(1.0 - distant.near.w, 1.0, textureSample(water_noise, noise_sampler, input.place + time).g);
    let twinkle = mix(1.0 - distant.alpha.z, 1.0, textureSample(water_noise, noise_sampler, input.place / 512.0 + time).g);
    return vec4f(input.colour * flicker * twinkle * t2 * t2, 1.0);
}
