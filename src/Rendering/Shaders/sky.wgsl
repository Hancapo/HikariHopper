// The sky the viewer lights drawables with, ported from RedFury's atmosphere.wgsl (Hillaire, "A Scalable and
// Production Ready Sky and Atmosphere Rendering Technique", EGSR 2020): the same medium, tables and integration. It
// reads the sky uniform below in place of RedFury's scene, and has no height fog. Shared by sky_luts.wgsl, which
// builds the tables, and by surface.wgsl and tonemap.wgsl through the harmonics they leave.

struct Sky {
    // Unit direction towards the key light, Z-up.
    sun_direction: vec4f,
    // Top-of-atmosphere illuminance of the key light, in the renderer's radiance units.
    light_illuminance: vec4f,
    // The eye; only its altitude in metres (z) is read.
    camera_position: vec4f,
};

@group(0) @binding(0) var<uniform> sky: Sky;

const ATMOSPHERE_PI: f32 = 3.14159265359;
const BOTTOM_RADIUS: f32 = 6360.0;
const TOP_RADIUS: f32 = 6460.0;
const RAYLEIGH_SCATTERING: vec3f = vec3f(0.005802, 0.013558, 0.0331);
const RAYLEIGH_SCALE_HEIGHT: f32 = 8.0;
const MIE_SCATTERING: vec3f = vec3f(0.003996);
const MIE_EXTINCTION: vec3f = vec3f(0.00444);
const MIE_SCALE_HEIGHT: f32 = 1.2;
const MIE_G: f32 = 0.8;
const OZONE_ABSORPTION: vec3f = vec3f(0.00065, 0.001881, 0.000085);
const GROUND_ALBEDO: vec3f = vec3f(0.3);
const PLANET_RADIUS_OFFSET: f32 = 0.01;
const TRANSMITTANCE_LUT_SIZE: vec2f = vec2f(256.0, 64.0);
const MULTI_SCATTERING_LUT_SIZE: f32 = 32.0;
const SKY_VIEW_LUT_SIZE: vec2f = vec2f(192.0, 108.0);
const AP_SLICE_COUNT: f32 = 32.0;
const AP_KM_PER_SLICE: f32 = 4.0;

struct Medium {
    scattering: vec3f,
    extinction: vec3f,
    rayleigh: vec3f,
    mie: vec3f,
};

fn sample_medium(height: f32) -> Medium {
    let rayleigh_density = exp(-height / RAYLEIGH_SCALE_HEIGHT);
    let mie_density = exp(-height / MIE_SCALE_HEIGHT);
    // Ozone: a tent centred at 25 km.
    let ozone_density = max(0.0, select(-height / 15.0 + 8.0 / 3.0, height / 15.0 - 2.0 / 3.0, height < 25.0));
    let rayleigh = RAYLEIGH_SCATTERING * rayleigh_density;
    let mie = MIE_SCATTERING * mie_density;
    return Medium(rayleigh + mie, rayleigh + MIE_EXTINCTION * mie_density + OZONE_ABSORPTION * ozone_density, rayleigh, mie);
}

// Nearest non-negative intersection with a planet-centred sphere, or -1.
fn ray_sphere(origin: vec3f, direction: vec3f, radius: f32) -> f32 {
    let b = dot(origin, direction);
    let c = dot(origin, origin) - radius * radius;
    let discriminant = b * b - c;
    if (discriminant < 0.0) {
        return -1.0;
    }
    let s = sqrt(discriminant);
    let near = -b - s;
    let far = -b + s;
    if (near >= 0.0) {
        return near;
    }
    return select(-1.0, far, far >= 0.0);
}

fn rayleigh_phase(cos_theta: f32) -> f32 {
    return 3.0 / (16.0 * ATMOSPHERE_PI) * (1.0 + cos_theta * cos_theta);
}

fn henyey_greenstein_phase(g: f32, cos_theta: f32) -> f32 {
    let denominator = 1.0 + g * g - 2.0 * g * cos_theta;
    return (1.0 - g * g) / (4.0 * ATMOSPHERE_PI * denominator * sqrt(denominator));
}

// Keeps bilinear lookups inside texel centres at the LUT borders.
fn unit_to_sub_uv(u: f32, resolution: f32) -> f32 {
    return (u + 0.5 / resolution) * (resolution / (resolution + 1.0));
}

fn sub_uv_to_unit(u: f32, resolution: f32) -> f32 {
    return (u - 0.5 / resolution) * (resolution / (resolution - 1.0));
}

// Bruneton's transmittance parameterization: x maps the distance to the atmosphere boundary, y the height.
fn transmittance_uv(view_height: f32, view_zenith_cos: f32) -> vec2f {
    let horizon = sqrt(TOP_RADIUS * TOP_RADIUS - BOTTOM_RADIUS * BOTTOM_RADIUS);
    let rho = sqrt(max(0.0, view_height * view_height - BOTTOM_RADIUS * BOTTOM_RADIUS));
    let discriminant = view_height * view_height * (view_zenith_cos * view_zenith_cos - 1.0) + TOP_RADIUS * TOP_RADIUS;
    let distance = max(0.0, -view_height * view_zenith_cos + sqrt(max(discriminant, 0.0)));
    let distance_min = TOP_RADIUS - view_height;
    let distance_max = rho + horizon;
    return vec2f((distance - distance_min) / (distance_max - distance_min), rho / horizon);
}

fn transmittance_parameters(uv: vec2f) -> vec2f {
    let horizon = sqrt(TOP_RADIUS * TOP_RADIUS - BOTTOM_RADIUS * BOTTOM_RADIUS);
    let rho = horizon * uv.y;
    let view_height = sqrt(rho * rho + BOTTOM_RADIUS * BOTTOM_RADIUS);
    let distance_min = TOP_RADIUS - view_height;
    let distance_max = rho + horizon;
    let distance = distance_min + uv.x * (distance_max - distance_min);
    var cosine = 1.0;
    if (distance > 0.0) {
        cosine = clamp((horizon * horizon - rho * rho - distance * distance) / (2.0 * view_height * distance), -1.0, 1.0);
    }
    return vec2f(view_height, cosine);
}

fn sample_transmittance(lut: texture_2d<f32>, filtering: sampler, view_height: f32, view_zenith_cos: f32) -> vec3f {
    return textureSampleLevel(lut, filtering, transmittance_uv(view_height, view_zenith_cos), 0.0).rgb;
}

fn sample_multiple_scattering(lut: texture_2d<f32>, filtering: sampler, view_height: f32, sun_zenith_cos: f32) -> vec3f {
    let uv = vec2f(sun_zenith_cos * 0.5 + 0.5, (view_height - BOTTOM_RADIUS) / (TOP_RADIUS - BOTTOM_RADIUS));
    let sub = vec2f(unit_to_sub_uv(clamp(uv.x, 0.0, 1.0), MULTI_SCATTERING_LUT_SIZE), unit_to_sub_uv(clamp(uv.y, 0.0, 1.0), MULTI_SCATTERING_LUT_SIZE));
    return textureSampleLevel(lut, filtering, sub, 0.0).rgb;
}

// Eye position in atmosphere space from the scene camera's altitude in metres.
fn atmosphere_eye() -> vec3f {
    return vec3f(0.0, 0.0, BOTTOM_RADIUS + max(sky.camera_position.z * 0.001, PLANET_RADIUS_OFFSET));
}

// Sky-view parameterization: latitude is compressed towards the horizon, where detail concentrates.
fn sky_view_uv(intersects_ground: bool, view_zenith_cos: f32, light_view_cos: f32, view_height: f32) -> vec2f {
    let horizon = sqrt(max(view_height * view_height - BOTTOM_RADIUS * BOTTOM_RADIUS, 0.0));
    let beta = acos(clamp(horizon / view_height, -1.0, 1.0));
    let zenith_horizon_angle = ATMOSPHERE_PI - beta;
    var v: f32;
    if (!intersects_ground) {
        let coordinate = 1.0 - sqrt(max(1.0 - acos(clamp(view_zenith_cos, -1.0, 1.0)) / zenith_horizon_angle, 0.0));
        v = coordinate * 0.5;
    } else {
        let coordinate = sqrt(max((acos(clamp(view_zenith_cos, -1.0, 1.0)) - zenith_horizon_angle) / beta, 0.0));
        v = coordinate * 0.5 + 0.5;
    }
    let u = sqrt(clamp(-light_view_cos * 0.5 + 0.5, 0.0, 1.0));
    return vec2f(unit_to_sub_uv(u, SKY_VIEW_LUT_SIZE.x), unit_to_sub_uv(v, SKY_VIEW_LUT_SIZE.y));
}

// Cosine of the horizontal angle between a direction and the sun, as stored by the sky-view LUT.
fn light_view_cosine(direction: vec3f, sun: vec3f) -> f32 {
    let d = direction.xy;
    let s = sun.xy;
    if (dot(d, d) < 1e-10 || dot(s, s) < 1e-10) {
        return 1.0;
    }
    return dot(normalize(d), normalize(s));
}

fn sky_luminance(lut: texture_2d<f32>, filtering: sampler, direction: vec3f) -> vec3f {
    let eye = atmosphere_eye();
    let view_height = length(eye);
    let intersects_ground = ray_sphere(eye, direction, BOTTOM_RADIUS) >= 0.0;
    let uv = sky_view_uv(intersects_ground, direction.z, light_view_cosine(direction, sky.sun_direction.xyz), view_height);
    return textureSampleLevel(lut, filtering, uv, 0.0).rgb;
}

struct Scattering {
    luminance: vec3f,
    transmittance: vec3f,
};

// RedFury's height fog, which the viewer leaves out: none, so its terms vanish.
fn fog_extinction(height: f32) -> f32 {
    return 0.0;
}

// Optical depth from a point to space through the fog layer: the vertical column over the cosine,
// clamped so a grazing sun is strongly but finitely attenuated.
fn fog_column(height: f32, cos_zenith: f32) -> f32 {
    return 0.0;
}

struct ScatteringStep {
    luminance: vec3f,
    segment: vec3f,
};

// One segment of length dt at p: sun single scattering (Rayleigh, Mie and fog phases), the
// multiple-scattering LUT for every medium, and the analytic in-segment integral.
fn scattering_step(transmittance_lut: texture_2d<f32>, multi_lut: texture_2d<f32>, filtering: sampler,
    p: vec3f, sun: vec3f, cos_theta: f32, dt: f32) -> ScatteringStep {
    let height = length(p);
    let altitude = height - BOTTOM_RADIUS;
    let medium = sample_medium(altitude);
    let fog = fog_extinction(altitude);
    let fog_scattering = 0.0;
    let extinction = medium.extinction + vec3f(fog);
    let scattering = medium.scattering + vec3f(fog_scattering);
    let segment = exp(-extinction * dt);
    let sun_zenith_cos = dot(sun, p / height);
    let sun_transmittance = sample_transmittance(transmittance_lut, filtering, height, sun_zenith_cos) * exp(-fog_column(altitude, sun_zenith_cos));
    let planet_shadow = select(1.0, 0.0, ray_sphere(p, sun, BOTTOM_RADIUS) >= 0.0);
    let multiple = sample_multiple_scattering(multi_lut, filtering, height, sun_zenith_cos);
    let phase_scattering = medium.mie * henyey_greenstein_phase(MIE_G, cos_theta) + medium.rayleigh * rayleigh_phase(cos_theta)
        + vec3f(fog_scattering * henyey_greenstein_phase(MIE_G, cos_theta));
    let source = sky.light_illuminance.rgb * (planet_shadow * sun_transmittance * phase_scattering + multiple * scattering);
    // Energy-conserving analytic integration of the source over the segment.
    return ScatteringStep((source - source * segment) / max(extinction, vec3f(1e-7)), segment);
}

// Single scattering plus the multiple-scattering LUT (the paper's Psi_ms), marched from origin along
// direction up to max_distance kilometres. Optionally adds the lit ground where the ray ends on it.
fn integrate_scattering(transmittance_lut: texture_2d<f32>, multi_lut: texture_2d<f32>, filtering: sampler,
    origin: vec3f, direction: vec3f, sun: vec3f, sample_count: f32, max_distance: f32, variable_samples: bool, with_ground: bool) -> Scattering {
    var result = Scattering(vec3f(0.0), vec3f(1.0));
    let bottom = ray_sphere(origin, direction, BOTTOM_RADIUS);
    let top = ray_sphere(origin, direction, TOP_RADIUS);
    var t_max = 0.0;
    if (bottom < 0.0) {
        if (top < 0.0) {
            return result;
        }
        t_max = top;
    } else {
        t_max = select(bottom, min(top, bottom), top > 0.0);
    }
    let hits_ground = bottom >= 0.0 && t_max == bottom;
    t_max = min(t_max, max_distance);
    var count = sample_count;
    if (variable_samples) {
        count = mix(4.0, sample_count, clamp(t_max * 0.01, 0.0, 1.0));
    }
    let cos_theta = dot(sun, direction);
    var throughput = vec3f(1.0);
    for (var s = 0.0; s < count; s += 1.0) {
        var dt: f32;
        var sample_t: f32;
        if (variable_samples) {
            var t0 = s / count;
            var t1 = (s + 1.0) / count;
            t0 = t0 * t0 * t_max;
            t1 = select(t1 * t1 * t_max, t_max, t1 > 1.0);
            sample_t = t0 + (t1 - t0) * 0.3;
            dt = t1 - t0;
        } else {
            dt = t_max / count;
            sample_t = t_max * (s + 0.3) / count;
        }
        let marched = scattering_step(transmittance_lut, multi_lut, filtering, origin + direction * sample_t, sun, cos_theta, dt);
        result.luminance += throughput * marched.luminance;
        throughput *= marched.segment;
    }
    if (with_ground && hits_ground) {
        let p = origin + direction * t_max;
        let height = length(p);
        let normal = p / height;
        let sun_zenith_cos = dot(normal, sun);
        let sun_transmittance = sample_transmittance(transmittance_lut, filtering, height, sun_zenith_cos) * exp(-fog_column(height - BOTTOM_RADIUS, sun_zenith_cos));
        result.luminance += throughput * sky.light_illuminance.rgb * sun_transmittance * clamp(sun_zenith_cos, 0.0, 1.0) * GROUND_ALBEDO / ATMOSPHERE_PI;
    }
    result.transmittance = throughput;
    return result;
}
