// Red Dead Redemption 2's sky behind its world, as its sky_dome effect draws it (vs/ps_sky_normal_moon_stars, then
// ps_sun_scattering and ps_moon; checked against the game's compiled programs), from the tables rdr2_sky_tables.wgsl
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
//   - The moon's disc, where it's drawn: its texture on a sphere lit from the sun, times its colour and intensity and
//     the same curved transmittance, added to the dome.
// The game works the scattering out at the dome's vertices and draws the discs as quads; here, each at its pixels. The
// dome's radiance is then scaled into the light the viewer draws the world with: its zenith (the scattering, the air
// glow's mean and the base colour there) to the viewer's sky ambient, as the world under it is lit by that.
// Put after scene.wgsl and rdr2_atmosphere.wgsl.

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

// The dome's radiance at the zenith, without the stars and discs: what it's scaled by into the viewer's light.
fn rdr2_zenith(height: f32) -> f32 {
    let night = dome.night;
    let at = dome.atmosphere;
    let zenith = vec3f(0.0, 0.0, 1.0);
    let scattered = rdr2_scattering(zenith, height);
    let phases = rdr2_phases(at.sun.z);
    var colour = scattered.rgb * at.sun_sky.rgb * phases.x + rdr2_mie(scattered) * at.sun_sun.rgb * phases.y;
    if (night.flags.y > 0.5) {
        let lit = rdr2_moon_scattering(zenith, height, night.moon.xyz);
        let moon_phases = rdr2_phases(night.moon.z);
        colour += lit.rgb * night.moon_sky.rgb * moon_phases.x + rdr2_mie(lit) * night.moon_sun.rgb * moon_phases.y;
    }
    colour = clamp(colour, vec3f(0.0), vec3f(at.sun_sky.w));
    // The air glow's mean: its smallest mip.
    let levels = f32(textureNumLevels(dome_air_glow) - 1u);
    colour += night.air_glow.rgb * textureSampleLevel(dome_air_glow, dome_wrap, vec2f(0.5), levels).r + night.sky_base.rgb;
    return dot(colour, vec3f(0.2126, 0.7152, 0.0722));
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
    // The moon's, a sphere facing the camera lit from the sun: where the view meets the plane across it, by its radius.
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
            let lit = max(dot(normal, at.sun.xyz), 0.0);
            colour += texel.rgb * night.moon_disc.rgb * lit * texel.a * rdr2_curved(transmittance, view);
        }
    }
    return vec4f(colour * at.disc.w / max(rdr2_zenith(height), 1e-9), 1.0);
}
