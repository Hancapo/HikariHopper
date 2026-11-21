// Red Dead Redemption 2's sky behind its world, as its sky_dome effect draws it (vs/ps_sky_normal_moon_stars, then
// ps_sun_scattering and ps_moon; checked against the game's compiled programs), from the tables Rdr2/sky_tables.wgsl
// builds, on the game's dome (a unit sphere, drawn round the camera at the dome's radius):
//   - The sun's scattering by the cosine to it, between its 8 slices along a cubic B-spline over four of them; the
//     Rayleigh part lit by the sun's intensity times the sky colour, 3/4 (1 + nu^2) its phase; the Mie part, its colour
//     from the Mie alpha by the Rayleigh's, lit by the sun's intensity times the sun colour, Cornette-Shanks its phase
//     times the Mie intensity. Where the moon's drawn, its own the same way, between two slices, lit by its intensity.
//     Together clamped to the sun's and the moon's intensities.
//   - The stars, where they're drawn: turned as the sky turns, the galaxy's band (the stars texture's red, round the
//     sky by its bearing), the dense stars in it (its alpha, on a cube's faces, fading towards the galaxy's poles,
//     twinkling) and the sparse stars everywhere (its green, blinking); faded in above the horizon, through the air
//     along the view, and hidden behind the moon.
//   - The air glow over the dome's coordinates, and the sky's base colour.
//   - The sun's disc, where it's drawn: the transmittance along the view, curved towards the horizon (raised to
//     1 + (1 - mu)^power (multiplier - 1)), lit by the sun's intensity times the sun colour times the disc's multiplier,
//     its limb darkened, at most 50000, added to the dome.
//   - The moon's disc, where it's drawn: its texture on a sphere lit from its phase's way, times its colour and
//     intensity and the same curved transmittance, added to the dome. The viewer's moon is full, as its GTA V sky's
//     is: lit from the camera.
// The game works the scattering out at the dome's vertices and draws the discs as quads; here, each at its pixels, in
// the game's own units, as its world is lit and exposed in them.
// And the sky's light on the world, each frame (cs_rdr2_sky_light): the key light's illuminance (the sun's or the
// moon's intensity and colour through the air from the camera, times the timecycle's light_dir tint), and the sky's
// ambient cube, as the game's CS_AmbientCubeIBR builds it from 64 ways over the upper half of the sky, its ground face
// the light a mid-grey ground under it sends back up.
// Put after Scene/scene.wgsl and Rdr2/atmosphere.wgsl.

struct Rdr2Night {
    // Whether the sun, the moon and the stars are drawn; the dome's radius in metres.
    flags: vec4f,
    // The unit way towards the moon, then the tangent of its disc's radius.
    moon: vec4f,
    // The moon's intensity times the sky colour (what its Rayleigh scattering is lit by), then the cosine of its
    // disc's radius.
    moon_sky: vec4f,
    // The moon's intensity times its scattering colour (what its Mie scattering is lit by).
    moon_sun: vec4f,
    // The moon disc's colour times its intensity.
    moon_disc: vec4f,
    // The stars' layers: the galaxy's (its colour by its intensity, then how it fades towards its poles), the dense
    // stars' (the same), and the sparse stars' (then how they fade towards the poles).
    stars_cloud: vec4f,
    stars_dense: vec4f,
    stars_sparse: vec4f,
    // The dense and the sparse stars' tiling, the twinkling's time, the noise's tiling.
    stars_tiling: vec4f,
    // One over the heights the stars fade in over and stop twinkling over, then the sparse stars' blink gain and bias.
    stars_fade: vec4f,
    // How the stars are turned: a way's row times each.
    stars_rotation: array<vec4f, 3>,
    // The air glow's colour by its intensity, then the sky's base colour by its intensity.
    air_glow: vec4f,
    sky_base: vec4f,
    // The camera, in the world.
    camera: vec4f,
    // The timecycle's tint of the key light (light_dir_col times light_dir_mult), then whether the sun is the key light
    // (else the moon).
    key_light: vec4f,
    // The camera's rays: its forward way, with how far the aerial perspective reaches; its right and up ways at the
    // frame's edge, the latter with the near plane.
    rays: array<vec4f, 3>,
    // The height fog (the local atmosphere's): its density a metre, the height it's thickest below and one over the
    // height it thins out over, its anisotropy; the tint of the key light through it, with how much the sun's shadow in
    // it is lifted; the tint of the sky's ambient through it, and its fill ambient.
    fog: array<vec4f, 4>,
    // The camera relative to the scene's origin, where the world's shadow is cast from.
    relative_camera: vec4f,
    // The far haze: where it starts, one over how far it builds up, its power, how much of the sky's ambient it takes
    // in (over the isotropic phase's divisor).
    far_haze: vec4f,
};

struct Rdr2Dome {
    atmosphere: Rdr2Atmosphere,
    orders: vec4f,
    night: Rdr2Night,
};

@group(1) @binding(0) var<uniform> dome: Rdr2Dome;
@group(1) @binding(1) var dome_sampler: sampler;
@group(1) @binding(2) var dome_transmittance: texture_2d<f32>;
@group(1) @binding(3) var dome_scattering: texture_3d<f32>;
@group(1) @binding(4) var dome_wrap: sampler;
@group(1) @binding(5) var dome_stars: texture_2d<f32>;
@group(1) @binding(6) var dome_air_glow: texture_2d<f32>;
@group(1) @binding(7) var dome_moon: texture_2d<f32>;
// The light gathered at a point from every order of the scattering, by the multiple scattering's intensity (the game's
// summed gathered light), as the tables' gathered light is laid out.
@group(1) @binding(9) var dome_gathered: texture_3d<f32>;

struct Rdr2SkyVarying {
    @builtin(position) position: vec4f,
    // The dome's vertex, from its centre at the camera.
    @location(0) place: vec3f,
    // The air glow's coordinates: the dome's, twice.
    @location(1) glow: vec2f,
    // The way to the vertex as the stars are turned, then how much they twinkle there.
    @location(2) stars: vec4f,
};

@vertex
fn vs_rdr2_sky(@location(0) place: vec3f, @location(1) uv: vec2f) -> Rdr2SkyVarying {
    let night = dome.night;
    let radius = night.flags.w;
    let world = scene.eye.xyz + place * radius;
    var out: Rdr2SkyVarying;
    out.position = scene.view_projection * vec4f(world - scene.origin.xyz, 1.0);
    // Reversed Z: at the far plane, behind everything the world draws over it.
    out.position.z = 0.0;
    out.place = place;
    out.glow = uv * 2.0;
    let way = normalize(place);
    let turned = way.x * night.stars_rotation[0].xyz + way.y * night.stars_rotation[1].xyz + way.z * night.stars_rotation[2].xyz;
    // A phase from the vertex's coordinates, the twinkle less towards the top of the sky.
    let phase = sin(fract(sin(dot(uv, vec2f(12.9898, 78.233002))) * 43758.546875) * 3.141593 + night.stars_tiling.z);
    let high = 1.0 - saturate(world.z * night.stars_fade.y);
    out.stars = vec4f(turned, high * high * (abs(phase) - 1.0) + 1.0);
    return out;
}

// The sun's scattering towards a view from a height, as vs_sky_normal_none reads it: the Rayleigh part in colour, the
// Mie in alpha.
fn rdr2_scattering(view: vec3f, height: f32) -> vec4f {
    let up = rdr2_view_u(view.z, height);
    let deep = sqrt(height / RDR2_DEPTH);
    let sun_u = rdr2_sun_u(dome.atmosphere.sun.z);
    let to_sun = dot(view, dome.atmosphere.sun.xyz) * 0.5 + 0.5;
    let slice = floor(to_sun * 7.0);
    let t = to_sun * 7.0 - slice;
    let s = 1.0 - t;
    // The cubic B-spline's weights over the slices before, at, after and two after, as the game normalises them.
    var weights = vec4f(s * s * s, 3.0 * t * t * t - 6.0 * t * t + 4.0, 3.0 * s * s * s - 6.0 * s * s + 4.0, t * t * t) / 6.0;
    weights /= weights.x + weights.y + weights.z + weights.w;
    let slices = clamp(vec4f(slice - 1.0, slice, slice + 1.0, slice + 2.0), vec4f(0.0), vec4f(7.0));
    var sum = vec4f(0.0);
    for (var i = 0; i < 4; i++) {
        sum += weights[i] * textureSampleLevel(dome_scattering, dome_sampler, vec3f((sun_u + slices[i]) * 0.125, up, deep), 0.0);
    }
    return sum;
}

// The moon's, as vs_sky_normal_moon reads it: between the two slices either side.
fn rdr2_moon_scattering(view: vec3f, height: f32, moon: vec3f) -> vec4f {
    let up = rdr2_view_u(view.z, height);
    let deep = sqrt(height / RDR2_DEPTH);
    let moon_u = rdr2_sun_u(moon.z);
    let to_moon = dot(view, moon) * 0.5 + 0.5;
    let slice = floor(to_moon * 7.0);
    let a = textureSampleLevel(dome_scattering, dome_sampler, vec3f((moon_u + slice) * 0.125, up, deep), 0.0);
    let b = textureSampleLevel(dome_scattering, dome_sampler, vec3f((moon_u + slice + 1.0) * 0.125, up, deep), 0.0);
    return mix(a, b, to_moon * 7.0 - slice);
}

// The Mie scattering's colour from its alpha, by the Rayleigh's.
fn rdr2_mie(scattered: vec4f) -> vec3f {
    let rayleigh = dome.atmosphere.rayleigh_mie;
    return scattered.w * scattered.rgb / max(scattered.r, 0.0001) * (rayleigh.x / rayleigh.xyz);
}

// Rayleigh's phase and the Mie's (Cornette-Shanks, by the Mie intensity) at a cosine.
fn rdr2_phases(nu: f32) -> vec2f {
    let phase = dome.atmosphere.phase;
    return vec2f(0.75 * (1.0 + nu * nu), phase.x * (1.0 + nu * nu) / pow(phase.y * nu + phase.z, 1.5) * phase.w);
}

// The stars along a turned way, as ps_sky_normal_stars draws them, before they're faded and hidden.
fn rdr2_stars(turned: vec3f, twinkle: f32) -> vec3f {
    let night = dome.night;
    let d = normalize(turned);
    // The galaxy's band, round the sky by its bearing (twice) and up it by its height.
    let bearing = atan2(d.x, d.y);
    let galaxy = textureSampleLevel(dome_stars, dome_wrap, vec2f(bearing * 0.318310 + 0.5, d.z * 0.5 + 0.5), 0.0).r;
    // The stars on a cube's faces, its major axis's.
    let size = abs(d);
    var face: vec3f;
    if (size.z >= size.x && size.z >= size.y) {
        face = vec3f(2.0 * d.z, select(d.x, -d.x, d.z < 0.0), -d.y);
    } else if (size.y >= size.x) {
        face = vec3f(2.0 * d.y, d.x, select(d.z, -d.z, d.y < 0.0));
    } else {
        face = vec3f(2.0 * d.x, select(-d.z, d.z, d.x < 0.0), -d.y);
    }
    let uv = face.yz / abs(face.x) + 0.5;
    let sparse_sample = textureSample(dome_stars, dome_wrap, uv * night.stars_tiling.y).g;
    let dense_sample = textureSample(dome_stars, dome_wrap, uv * night.stars_tiling.x).a;
    let polar = 1.0 - abs(d.z);
    let blink = saturate(night.stars_fade.z * (2.0 * twinkle - 1.0) + night.stars_fade.w);
    let band = galaxy * saturate(polar * night.stars_cloud.w);
    let sparse = sparse_sample * sparse_sample * (1.0 + sqrt(abs(d.z)) * (night.stars_sparse.w - 1.0)) * blink;
    let dense = dense_sample * dense_sample * saturate(polar * night.stars_dense.w) * twinkle * band;
    return dense * night.stars_dense.rgb + sparse * night.stars_sparse.rgb + band * band * band * night.stars_cloud.rgb;
}

// The transmittance along a view from a height, as the dome and the discs read it.
fn rdr2_view_transmittance(view: vec3f, height: f32) -> vec3f {
    return textureSampleLevel(dome_transmittance, dome_sampler,
        vec2f(rdr2_transmittance_u(view.z), sqrt(min(height, RDR2_DEPTH) * dome.atmosphere.heights.x / RDR2_DEPTH)), 0.0).rgb;
}

// That transmittance curved towards the horizon, as the discs are dimmed by it.
fn rdr2_curved(transmittance: vec3f, view: vec3f) -> vec3f {
    let disc = dome.atmosphere.disc;
    return pow(transmittance, vec3f(1.0 + pow(1.0 - saturate(view.z), disc.y) * (disc.x - 1.0)));
}

// The sky's radiance along a view, without the stars and discs: the sun's and the moon's scattering, clamped, then the
// air glow (its mean, its smallest mip) and the base colour.
fn rdr2_sky_radiance(view: vec3f, height: f32) -> vec3f {
    let night = dome.night;
    let at = dome.atmosphere;
    let scattered = rdr2_scattering(view, height);
    let phases = rdr2_phases(dot(view, at.sun.xyz));
    var colour = scattered.rgb * at.sun_sky.rgb * phases.x + rdr2_mie(scattered) * at.sun_sun.rgb * phases.y;
    if (night.flags.y > 0.5) {
        let lit = rdr2_moon_scattering(view, height, night.moon.xyz);
        let moon_phases = rdr2_phases(dot(view, night.moon.xyz));
        colour += lit.rgb * night.moon_sky.rgb * moon_phases.x + rdr2_mie(lit) * night.moon_sun.rgb * moon_phases.y;
    }
    colour = clamp(colour, vec3f(0.0), vec3f(at.sun_sky.w));
    let levels = f32(textureNumLevels(dome_air_glow) - 1u);
    return colour + night.air_glow.rgb * textureSampleLevel(dome_air_glow, dome_wrap, vec2f(0.5), levels).r + night.sky_base.rgb;
}

@fragment
fn fs_rdr2_sky(input: Rdr2SkyVarying) -> @location(0) vec4f {
    let night = dome.night;
    let at = dome.atmosphere;
    let view = normalize(input.place);
    // The camera's height over the game's ground for the sky: from the altitude bias, either way.
    let height = abs(at.heights.w - scene.eye.z);
    let scattered = rdr2_scattering(view, height);
    let nu = dot(view, at.sun.xyz);
    let phases = rdr2_phases(nu);
    var colour = scattered.rgb * at.sun_sky.rgb * phases.x + rdr2_mie(scattered) * at.sun_sun.rgb * phases.y;
    let moon = night.moon.xyz;
    let to_moon = dot(view, moon);
    let moon_drawn = night.flags.y > 0.5;
    if (moon_drawn) {
        let lit = rdr2_moon_scattering(view, height, moon);
        let moon_phases = rdr2_phases(to_moon);
        colour += lit.rgb * night.moon_sky.rgb * moon_phases.x + rdr2_mie(lit) * night.moon_sun.rgb * moon_phases.y;
    }
    colour = clamp(colour, vec3f(0.0), vec3f(at.sun_sky.w));
    let transmittance = rdr2_view_transmittance(view, height);

    if (night.flags.z > 0.5) {
        let above = saturate(input.place.z * night.flags.w * night.stars_fade.x);
        let hidden = moon_drawn && to_moon > night.moon_sky.w;
        colour += select(rdr2_stars(input.stars.xyz, input.stars.w) * above * above * transmittance, vec3f(0.0), hidden);
    }
    colour += night.air_glow.rgb * textureSample(dome_air_glow, dome_wrap, input.glow).r + night.sky_base.rgb;

    // The sun's disc, where the view is within its radius.
    if (night.flags.x > 0.5 && nu > 0.0) {
        let across = sqrt(max(1.0 - nu * nu, 0.0)) / nu / at.sun.w;
        if (across < 1.0) {
            let limb = saturate(1.0 - at.disc.z * (1.0 - sqrt(1.0 - across * across)));
            colour += min(rdr2_curved(transmittance, view) * limb * at.sun_sun.rgb * at.sun_sun.w, vec3f(50000.0));
        }
    }
    // The moon's, a sphere facing the camera lit from it (full): where the view meets the plane across it, by its radius.
    let toward = -moon;
    let right = normalize(vec3f(-toward.y, toward.x, 0.0) + vec3f(1e-6, 0.0, 0.0));
    let up = cross(toward, right);
    let plane = (view / max(to_moon, 1e-4) - moon) / night.moon.w;
    let a = dot(plane, right);
    let b = dot(plane, up);
    let texel = textureSample(dome_moon, dome_wrap, vec2f(a * 0.5 + 0.5, 0.5 - b * 0.5));
    if (moon_drawn && to_moon > 0.0) {
        let r2 = a * a + b * b;
        if (r2 < 1.0) {
            let normal = a * right + b * up + sqrt(1.0 - r2) * toward;
            let lit = max(dot(normal, toward), 0.0);
            colour += texel.rgb * night.moon_disc.rgb * lit * texel.a * rdr2_curved(transmittance, view);
        }
    }
    return vec4f(colour * at.disc.w, 1.0);
}

// What the world is lit with: the key light's illuminance, with one in w; then the ambient cube's faces (+x, -x, +z,
// +y, -y, -z), each the irradiance over pi on what faces it.
@group(1) @binding(8) var<storage, read_write> sky_light: array<vec4f, 7>;

// Each way's share of the faces it's towards.
var<workgroup> faces: array<array<vec3f, 6>, 64>;

// Van der Corput's radical inverse in base two, as the game turns its sample's index round the sky.
fn rdr2_radical_inverse(index: u32) -> f32 {
    return f32(reverseBits(index)) * 2.3283064e-10;
}

@compute @workgroup_size(64)
fn cs_rdr2_sky_light(@builtin(local_invocation_index) index: u32) {
    let night = dome.night;
    let at = dome.atmosphere;
    let height = abs(at.heights.w - night.camera.z);
    // A way over the upper half of the sky: evenly up from the horizon, round by the radical inverse.
    let up = 1.0 - (f32(index) + 0.5) / 64.0;
    let across = sqrt(1.0 - up * up);
    let bearing = rdr2_radical_inverse(index) * 6.2831853;
    let way = vec3f(across * cos(bearing), across * sin(bearing), up);
    let radiance = rdr2_sky_radiance(way, height);
    let weights = way * way * 0.046875;
    var share: array<vec3f, 6>;
    for (var face = 0; face < 6; face++) {
        share[face] = vec3f(0.0);
    }
    share[select(0, 1, way.x <= 0.0)] += weights.x * radiance;
    share[select(3, 4, way.y <= 0.0)] += weights.y * radiance;
    share[2] += weights.z * radiance;
    faces[index] = share;
    workgroupBarrier();
    if (index != 0u) {
        return;
    }
    var cube: array<vec3f, 6>;
    for (var face = 0; face < 6; face++) {
        cube[face] = vec3f(0.0);
    }
    for (var i = 0; i < 64; i++) {
        for (var face = 0; face < 6; face++) {
            cube[face] += faces[i][face];
        }
    }
    // The key light through the air from the camera, by the timecycle's tint.
    let sun = night.key_light.w > 0.5;
    let toward = select(night.moon.xyz, at.sun.xyz, sun);
    let drawn = select(night.flags.y, night.flags.x, sun) > 0.5;
    let intensity = select(night.moon_sun.rgb, at.sun_sun.rgb, sun) * rdr2_view_transmittance(toward, height) * night.key_light.rgb;
    let illuminance = select(vec3f(0.0), intensity, drawn && toward.z > 0.0);
    // The ground under it, a mid-grey: what it sends back up from the key light and the sky.
    let ground = 0.25 * (illuminance * max(toward.z, 0.0) / 3.14159265 + cube[2]);
    sky_light[0] = vec4f(illuminance, 1.0);
    for (var face = 0; face < 5; face++) {
        sky_light[face + 1] = vec4f(cube[face], 0.0);
    }
    sky_light[6] = vec4f(ground, 0.0);
}
