// GTA V's sky in its world, as the game draws it (its sky_system effect, the same in Enhanced): the sky dome
// (vs/ps_sky_normal_*), the moon and the sun's disc over it (vs/ps_moon, vs/ps_sun), and the cloud noise the dome's
// clouds are made from, drawn first each frame. The constants are those the game derives from the
// timecycle (GtaSky.cs); the dome and its textures are the game's own (models/skydome.ydd, textures/skydome.ytd).
//   - The dome is the game's model, 20 km across, centred under the camera at the water's level. Its gradient and fog
//     are worked out per vertex, as the game's are; the sun's glow, the clouds, their light, the stars and the dither
//     per pixel.
//   - Fog is the game's height fog without its haze (Gta5/fog.wgsl's gta_fog_no_haze), as the sky's passes take it.
//   - The game draws the sky after the world, at the far plane, where nothing else was; here it goes first and the world
//     over it. Its alpha says how much of the sun and moon shows through (the horizon and the clouds hide them); once
//     they're drawn the sky covers the frame, which the tone map then takes whole.
//   - Left out: the sky plane under the horizon (the ocean covers it) and the fog rays. The cloud hats, the game's own
//     models drawn after the sky, are Gta5/cloud_hats.wgsl's.
// Put after Scene/scene.wgsl, Gta5/sky_system.wgsl and Gta5/fog.wgsl.

struct GtaSky {
    // azimuthEastColor, with azimuthTransitionPosition in w.
    azimuth_east: vec4f,
    // azimuthWestColor, with hdrIntensity in w.
    azimuth_west: vec4f,
    // azimuthTransitionColor, with horizonLevel (the water's) in w.
    azimuth_transition: vec4f,
    // zenithColor, with starfieldIntensity in w.
    zenith: vec4f,
    // zenithTransitionColor, with moonIntensity in w.
    zenith_transition: vec4f,
    // zenithConstants: the transition's height, its east and west blends, one less the zenith blend's start.
    zenith_constants: vec4f,
    // sunColor, with the sun's disc radius in metres at the dome in w.
    sun_colour: vec4f,
    // sunColorHdr, with the moon's disc radius in w.
    sun_colour_hdr: vec4f,
    // sunDiscColorHdr.
    sun_disc_colour_hdr: vec4f,
    // sunConstants: 2g, 1 + g², the glow's scale, its intensity.
    sun_constants: vec4f,
    // sunDirection and moonDirection, as the game sets them: where each is on the dome, from the world's origin.
    sun_direction: vec4f,
    moon_direction: vec4f,
    // sunPosition and moonPosition: where each is on the dome.
    sun_position: vec4f,
    moon_position: vec4f,
    // cloudBaseMinusMidColour, cloudMidColour, cloudShadowMinusBaseColourTimesShadowStrength.
    cloud_base_minus_mid: vec4f,
    cloud_mid: vec4f,
    cloud_shadow_minus_base: vec4f,
    // cloudDetailConstants: the edge detail's strength and scale, the overall detail's strength and scale.
    cloud_detail: vec4f,
    // cloudConstants1: base strength, density multiplier, density bias, fade out.
    cloud_constants1: vec4f,
    // cloudConstants2: shadow strength, offset, overall colour strength, HDR intensity.
    cloud_constants2: vec4f,
    // smallCloudConstants: detail scale, detail strength, density multiplier, density bias.
    small_cloud: vec4f,
    // smallCloudColorHdr.
    small_cloud_colour: vec4f,
    // effectsConstants: the sun's influence radius and scatter intensity, the moon's.
    effects: vec4f,
    // speedConstants: the small clouds', the overall detail's and the edge detail's offsets.
    speed: vec4f,
    // The lunar cycle: where the moon's light comes from, across its disc.
    lunar_cycle: vec4f,
    // moonColor.
    moon_colour: vec4f,
    // The cloud noise: noiseFrequency, noiseScale, noiseThreshold, noiseSoftness; then noiseDensityOffset and
    // noisePhase.
    noise: vec4f,
    noise_phase: vec4f,
    // Which of the game's passes are on: the sun, the moon, the clouds, the stars (one or nought each).
    passes: vec4f,
    // The fog (Gta5/fog.wgsl), as the game sets it.
    fog: GtaFog,
    // The camera, in the world.
    camera: vec4f,
};

@group(1) @binding(0) var<uniform> sky: GtaSky;
@group(1) @binding(1) var sky_wrap: sampler;
@group(1) @binding(2) var sky_clamp: sampler;
@group(1) @binding(3) var perlin_map: texture_2d<f32>;
@group(1) @binding(4) var high_detail_map: texture_2d<f32>;
@group(1) @binding(5) var dither_map: texture_2d<f32>;
@group(1) @binding(6) var star_map: texture_2d<f32>;
@group(1) @binding(7) var moon_map: texture_2d<f32>;

// The dome's and the sun's and moon's distance (DOME_RADIUS), and the distance their fog is taken at.
const DOME_RADIUS: f32 = 20000.0;
const DISC_FOG_DISTANCE: f32 = 25000.0;

// A clip position at the far plane (depth nought in reversed Z), as the game forces the sky's depth to it.
fn sky_clip(world: vec3f) -> vec4f {
    let clip = scene.view_projection * vec4f(world - scene.origin.xyz, 1.0);
    return vec4f(clip.xy, 0.0, clip.w);
}

// sphericalWarpOffset.
fn spherical_warp_offset(uv: vec2f, offset: vec2f) -> vec2f {
    let t = 2.0 * uv - 1.0;
    return offset * (1.0 - dot(t, t));
}

// From the east (0) to the west (1) by how far a direction faces west.
fn azimuth_blend(d: vec3f) -> f32 {
    return sqrt(max(-d.x * 0.5 + 0.5, 0.0));
}

// ---------------------------------------------------------------------------------------------------------------------
// The cloud noise: three octaves of the base noise, one per channel, as the game draws it into its
// 512 by 512 map each frame.

struct PerlinVarying {
    @builtin(position) position: vec4f,
    @location(0) uv: vec2f,
};

@group(1) @binding(0) var<uniform> perlin_sky: GtaSky;
@group(1) @binding(1) var perlin_wrap: sampler;
@group(1) @binding(2) var noise_map: texture_2d<f32>;

@vertex
fn vs_gta_perlin(@builtin(vertex_index) index: u32) -> PerlinVarying {
    let corner = vec2f(f32((index << 1u) & 2u), f32(index & 2u));
    var out: PerlinVarying;
    out.position = vec4f(corner * 2.0 - 1.0, 0.0, 1.0);
    // vs_perlin: the map's top-left is (0, 0).
    out.uv = vec2f(corner.x, 1.0 - corner.y);
    return out;
}

@fragment
fn fs_gta_perlin(in: PerlinVarying) -> @location(0) vec4f {
    let n = perlin_sky.noise;
    var p = ((in.uv - 0.5) * n.y + 0.5) / 128.0 + perlin_sky.noise_phase.yz;
    var total = 0.0;
    var amplitude = 1.0;
    for (var i = 0; i < 3; i++) {
        let value = (textureSample(noise_map, perlin_wrap, p).xyz - 0.5) * 2.0;
        total += value[i] * amplitude;
        p *= n.x;
        amplitude *= 0.5;
    }
    let noise = total / 1.75 * 0.5 + 0.5;
    let low = n.z - n.w;
    let high = n.z + n.w;
    let offset = perlin_sky.noise_phase.x;
    return vec4f(smoothstep(low, high, noise), smoothstep(low + offset, high + offset, noise), noise, 0.0);
}

// ---------------------------------------------------------------------------------------------------------------------
// The dome.

struct DomeIn {
    @location(0) position: vec3f,
    @location(1) uv0: vec2f,
    @location(2) uv1: vec2f,
};

struct DomeVarying {
    @builtin(position) position: vec4f,
    // The clouds' coordinates, and the base layer's, pushed away from the sun.
    @location(0) uv: vec4f,
    // Where the vertex is in the world, and how far above the horizon (the sun and moon fade below it).
    @location(1) world: vec4f,
    // The gradient, by the sky's brightness.
    @location(2) colour: vec3f,
    // The edge detail's coordinates, then the overall detail's.
    @location(3) detail: vec4f,
    // The dither's, then the small clouds'.
    @location(4) dither_small: vec4f,
    @location(5) stars: vec2f,
    @location(6) fog: vec4f,
};

@vertex
fn vs_gta_sky(in: DomeIn) -> DomeVarying {
    // The dome under the camera, at the water's level: the sun's height on it doesn't change with the camera's.
    let camera = sky.camera.xyz;
    let centre = vec3f(camera.xy, sky.azimuth_transition.w);
    let world = centre + in.position * DOME_RADIUS;
    var out: DomeVarying;
    out.position = sky_clip(world);
    out.fog = gta_fog_no_haze(sky.fog, world - camera);
    let view = normalize(world - centre);
    out.colour = sky_gradient(sky.azimuth_east.rgb, sky.azimuth_transition.rgb, sky.azimuth_west.rgb, sky.azimuth_east.w,
        sky.zenith.rgb, sky.zenith_transition.rgb, sky.zenith_constants, azimuth_blend(view), abs(view.z)) * sky.azimuth_west.w;
    out.world = vec4f(world, saturate(saturate(dot(normalize(world), vec3f(0.0, 0.0, 1.0))) / 0.2));
    let offset = (sky.cloud_constants2.y / 10.0) * (sky.sun_direction.xyz - view).xy;
    out.uv = vec4f(in.uv0, in.uv0 + spherical_warp_offset(in.uv0, -offset));
    out.detail = vec4f(((in.uv0 - 0.5) + sky.speed.z) * sky.cloud_detail.y, ((in.uv0.yx - 0.5) + sky.speed.y) * sky.cloud_detail.w);
    out.dither_small = vec4f(in.uv1 * 64.0, ((in.uv0 - 0.5) + sky.speed.x) * sky.small_cloud.x);
    out.stars = in.uv1 * 12.0;
    return out;
}

@fragment
fn fs_gta_sky(in: DomeVarying) -> @location(0) vec4f {
    let perlin = textureSample(perlin_map, sky_wrap, in.uv.xy).xz;
    let offset_perlin = textureSample(perlin_map, sky_wrap, in.uv.zw).xy;
    let detail_overall = textureSample(high_detail_map, sky_wrap, in.detail.zw).x - 0.5;
    let detail_edge = vec2f(textureSample(high_detail_map, sky_wrap, in.detail.xy).x, textureSample(high_detail_map, sky_wrap, in.dither_small.zw).x) - 0.5;
    let dither = textureSample(dither_map, sky_wrap, in.dither_small.xy).x - 0.5;
    let stars = textureSample(star_map, sky_wrap, in.stars).rgb;

    let view = normalize(in.world.xyz);
    let moon_angle = dot(view, sky.moon_direction.xyz);

    // The sun's glow, over the gradient rather than added to it.
    let sc = sky.sun_constants;
    let cos_theta = dot(view, -sky.sun_direction.xyz);
    let phase = (1.0 + cos_theta * cos_theta) / pow(sc.y - sc.x * cos_theta, 1.5) * sc.z;
    var colour = in.colour * saturate(1.0 - phase) + sky.sun_colour_hdr.rgb * saturate(phase) * sc.w;
    let sun_cos = saturate(-cos_theta);

    // The clouds: the large ones in x, the small filler ones in y.
    let c1 = sky.cloud_constants1;
    let c2 = sky.cloud_constants2;
    let cd = sky.cloud_detail;
    let sm = sky.small_cloud;
    var base = offset_perlin.x + detail_overall * c2.z * cd.z;
    base = base * c1.x * perlin.x;
    var detail = vec2f(detail_overall, 1.0 - perlin.y) * vec2f(cd.z, sm.y);
    detail += detail_edge * vec2f(cd.x, sm.y) * saturate(1.0 - base * 2.0) * perlin.y;
    let cloud = vec2f(perlin.x, 0.0) + detail;
    let density = saturate(vec2f(c1.y, sm.z) * cloud - vec2f(c1.z, sm.w));
    var amount = density * density * saturate(view.z * 5.0 - c1.w);
    let shadow = offset_perlin.y + detail.x;
    let cloud_colour = base * (shadow * sky.cloud_shadow_minus_base.rgb + sky.cloud_base_minus_mid.rgb) + sky.cloud_mid.rgb;
    let clouds_on = sky.passes.z > 0.5;
    amount = select(vec2f(0.0), amount, clouds_on);
    let dimming = select(0.0, amount.x + amount.y * 0.3 * saturate(1.0 - amount.x), clouds_on);
    let noise = select(perlin.x, cloud.x, clouds_on);
    colour = mix(colour, sky.small_cloud_colour.rgb, amount.y);
    colour = mix(colour, cloud_colour * c2.w, amount.x);

    // The sun's and moon's light through the clouds near them (calcEffectsData*).
    let influence = 1.0 - saturate((1.0 - vec2f(sun_cos, moon_angle)) / sky.effects.xz * 100.0);
    var scatter = influence * dimming * saturate(1.0 - noise);
    scatter *= scatter;
    scatter *= scatter;
    scatter *= vec2f(sky.effects.y, sky.effects.w * (sky.lunar_cycle.y * 0.5 + 0.5)) * sky.passes.xy;
    colour += sky.sun_colour.rgb * scatter.x + sky.moon_colour.rgb * scatter.y;

    // The stars, but not on the moon's disc nor behind clouds.
    let moon_disc = step(0.00054475, 1.0 - moon_angle);
    colour += saturate(stars * 2.0 - 0.1) * sky.zenith.w * moon_disc * (1.0 - dimming) * sky.passes.w;

    colour = mix(colour, in.fog.rgb, in.fog.a);
    colour += dither * 0.0001;
    return vec4f(colour, saturate(in.world.w - (amount.x + amount.y)));
}

// ---------------------------------------------------------------------------------------------------------------------
// The moon and the sun: discs on the dome facing the camera, upright, each a fan of 88 triangles as the game's
// (a triangle fan), made here from the vertex's number.

const DISC_SIDES: u32 = 88u;

// A corner of the disc's fan: its place across the disc, in the disc's radii.
fn disc_corner(index: u32) -> vec2f {
    let triangle = index / 3u;
    let corner = index % 3u;
    if (corner == 0u) {
        return vec2f(0.0);
    }
    let angle = f32(triangle + corner - 1u) * 6.2831853 / f32(DISC_SIDES);
    return vec2f(cos(angle), sin(angle));
}

// Where a corner of a disc of this radius at this place on the dome is: turned to face the camera, kept upright.
fn disc_world(place: vec3f, radius: f32, corner: vec2f) -> vec3f {
    let towards = normalize(sky.camera.xyz - place);
    let across = normalize(cross(vec3f(0.0, 0.0, 1.0), towards));
    let up = cross(towards, across);
    return place + across * corner.x * radius + up * corner.y * radius;
}

struct MoonVarying {
    @builtin(position) position: vec4f,
    @location(0) uv: vec2f,
    // How much of the moon shows: not where the fog or the horizon hides it.
    @location(1) shown: f32,
};

@vertex
fn vs_gta_moon(@builtin(vertex_index) index: u32) -> MoonVarying {
    let corner = disc_corner(index);
    let world = disc_world(sky.moon_position.xyz, sky.sun_colour_hdr.w, corner);
    var out: MoonVarying;
    out.position = sky_clip(world);
    // The fan's coordinates (half its radius across the map), flipped as vs_moon flips them.
    let uv = corner * 0.5 + 0.5;
    out.uv = vec2f(uv.x, 1.0 - uv.y);
    let fogged = saturate(1.0 - gta_fog_no_haze(sky.fog, normalize(world - sky.camera.xyz) * DISC_FOG_DISTANCE).a);
    out.shown = fogged * saturate(saturate(dot(normalize(world), vec3f(0.0, 0.0, 1.0))) / 0.2);
    return out;
}

@fragment
fn fs_gta_moon(in: MoonVarying) -> @location(0) vec4f {
    let brightness = textureSample(moon_map, sky_clamp, in.uv);
    // Lit as a sphere would be, by the lunar cycle's light.
    let across = (in.uv - 0.5) * 2.0;
    let normal = normalize(vec3f(across.x, sqrt(max(1.0 - dot(across, across), 0.0)), across.y));
    let light = max(0.0, dot(normal, normalize(sky.lunar_cycle.xyz)));
    let moon = brightness.rgb * sky.moon_colour.rgb * light * sky.zenith_transition.w;
    return vec4f(moon * brightness.a * in.shown, 1.0);
}

struct SunVarying {
    @builtin(position) position: vec4f,
    @location(0) fog: vec4f,
};

@vertex
fn vs_gta_sun(@builtin(vertex_index) index: u32) -> SunVarying {
    let world = disc_world(sky.sun_position.xyz, sky.sun_colour.w, disc_corner(index));
    var out: SunVarying;
    out.position = sky_clip(world);
    out.fog = gta_fog_no_haze(sky.fog, normalize(world - sky.camera.xyz) * DISC_FOG_DISTANCE);
    return out;
}

@fragment
fn fs_gta_sun(in: SunVarying) -> @location(0) vec4f {
    return vec4f(mix(sky.sun_disc_colour_hdr.rgb, in.fog.rgb, in.fog.a), 1.0);
}

// Once the sun and moon are drawn: the sky covers the frame.
@vertex
fn vs_gta_sky_cover(@builtin(vertex_index) index: u32) -> @builtin(position) vec4f {
    let corner = vec2f(f32((index << 1u) & 2u), f32(index & 2u));
    return vec4f(corner * 2.0 - 1.0, 0.0, 1.0);
}

@fragment
fn fs_gta_sky_cover() -> @location(0) vec4f {
    return vec4f(0.0, 0.0, 0.0, 1.0);
}
