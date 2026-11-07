// Compute passes for the sky in sky.wgsl, ported from RedFury's atmosphere_luts.wgsl. Transmittance and multiple
// scattering depend only on the atmosphere; sky view and its harmonics follow the sun. RedFury's aerial-perspective
// froxels are left out. Each pass binds only the group 1 entries it uses.
@group(1) @binding(0) var transmittance_lut: texture_2d<f32>;
@group(1) @binding(1) var multi_scattering_lut: texture_2d<f32>;
@group(1) @binding(2) var lut_sampler: sampler;
@group(1) @binding(3) var output_2d: texture_storage_2d<rgba16float, write>;
@group(1) @binding(5) var sky_view_lut: texture_2d<f32>;
@group(1) @binding(6) var<storage, read_write> sky_irradiance: array<vec4f, 9>;

@compute @workgroup_size(8, 8, 1)
fn cs_transmittance(@builtin(global_invocation_id) id: vec3u) {
    if (any(vec2f(id.xy) >= TRANSMITTANCE_LUT_SIZE)) {
        return;
    }
    let parameters = transmittance_parameters((vec2f(id.xy) + 0.5) / TRANSMITTANCE_LUT_SIZE);
    let origin = vec3f(0.0, 0.0, parameters.x);
    let direction = vec3f(0.0, sqrt(max(1.0 - parameters.y * parameters.y, 0.0)), parameters.y);
    let bottom = ray_sphere(origin, direction, BOTTOM_RADIUS);
    var t_max = ray_sphere(origin, direction, TOP_RADIUS);
    if (bottom >= 0.0) {
        t_max = min(t_max, bottom);
    }
    var optical_depth = vec3f(0.0);
    let steps = 40.0;
    let dt = max(t_max, 0.0) / steps;
    for (var s = 0.0; s < steps; s += 1.0) {
        let p = origin + direction * (s + 0.3) * dt;
        optical_depth += sample_medium(length(p) - BOTTOM_RADIUS).extinction * dt;
    }
    textureStore(output_2d, id.xy, vec4f(exp(-optical_depth), 1.0));
}

var<workgroup> shared_luminance: array<vec3f, 64>;
var<workgroup> shared_transfer: array<vec3f, 64>;

// One texel per workgroup, one of 64 uniformly distributed directions per invocation. The second-order
// luminance L2 and transfer f_ms give Psi_ms = L2 / (1 - f_ms), the infinite series of isotropic bounces.
@compute @workgroup_size(64, 1, 1)
fn cs_multiple_scattering(@builtin(workgroup_id) group: vec3u, @builtin(local_invocation_index) index: u32) {
    let uv = vec2f(sub_uv_to_unit((f32(group.x) + 0.5) / MULTI_SCATTERING_LUT_SIZE, MULTI_SCATTERING_LUT_SIZE),
        sub_uv_to_unit((f32(group.y) + 0.5) / MULTI_SCATTERING_LUT_SIZE, MULTI_SCATTERING_LUT_SIZE));
    let sun_zenith_cos = uv.x * 2.0 - 1.0;
    let view_height = BOTTOM_RADIUS + clamp(uv.y + PLANET_RADIUS_OFFSET, 0.0, 1.0) * (TOP_RADIUS - BOTTOM_RADIUS - PLANET_RADIUS_OFFSET);
    let origin = vec3f(0.0, 0.0, view_height);
    let sun = vec3f(0.0, sqrt(max(1.0 - sun_zenith_cos * sun_zenith_cos, 0.0)), sun_zenith_cos);
    let a = (0.5 + f32(index / 8u)) / 8.0;
    let b = (0.5 + f32(index % 8u)) / 8.0;
    let theta = 2.0 * ATMOSPHERE_PI * a;
    let phi = acos(1.0 - 2.0 * b);
    let direction = vec3f(cos(theta) * sin(phi), sin(theta) * sin(phi), cos(phi));
    let uniform_phase = 1.0 / (4.0 * ATMOSPHERE_PI);

    var luminance = vec3f(0.0);
    var transfer = vec3f(0.0);
    let bottom = ray_sphere(origin, direction, BOTTOM_RADIUS);
    let top = ray_sphere(origin, direction, TOP_RADIUS);
    var t_max = top;
    if (bottom >= 0.0) {
        t_max = select(bottom, min(top, bottom), top > 0.0);
    }
    if (t_max > 0.0) {
        let steps = 20.0;
        let dt = t_max / steps;
        var throughput = vec3f(1.0);
        for (var s = 0.0; s < steps; s += 1.0) {
            let p = origin + direction * t_max * (s + 0.3) / steps;
            let height = length(p);
            let medium = sample_medium(height - BOTTOM_RADIUS);
            let segment = exp(-medium.extinction * dt);
            let sun_transmittance = sample_transmittance(transmittance_lut, lut_sampler, height, dot(sun, p / height));
            let planet_shadow = select(1.0, 0.0, ray_sphere(p, sun, BOTTOM_RADIUS) >= 0.0);
            let source = planet_shadow * sun_transmittance * medium.scattering * uniform_phase;
            let extinction = max(medium.extinction, vec3f(1e-7));
            luminance += throughput * (source - source * segment) / extinction;
            transfer += throughput * (medium.scattering - medium.scattering * segment) / extinction;
            throughput *= segment;
        }
        if (bottom >= 0.0 && t_max == bottom) {
            let p = origin + direction * t_max;
            let normal = normalize(p);
            let sun_transmittance = sample_transmittance(transmittance_lut, lut_sampler, length(p), dot(sun, normal));
            luminance += throughput * sun_transmittance * clamp(dot(normal, sun), 0.0, 1.0) * GROUND_ALBEDO / ATMOSPHERE_PI;
        }
    }
    shared_luminance[index] = luminance;
    shared_transfer[index] = transfer;
    workgroupBarrier();
    for (var stride = 32u; stride > 0u; stride = stride >> 1u) {
        if (index < stride) {
            shared_luminance[index] += shared_luminance[index + stride];
            shared_transfer[index] += shared_transfer[index + stride];
        }
        workgroupBarrier();
    }
    if (index == 0u) {
        // Integrating over the sphere with an isotropic phase function reduces to the mean.
        let second_order = shared_luminance[0] / 64.0;
        let transfer_mean = shared_transfer[0] / 64.0;
        textureStore(output_2d, group.xy, vec4f(second_order / max(vec3f(1.0) - transfer_mean, vec3f(1e-4)), 1.0));
    }
}

@compute @workgroup_size(8, 8, 1)
fn cs_sky_view(@builtin(global_invocation_id) id: vec3u) {
    if (any(vec2f(id.xy) >= SKY_VIEW_LUT_SIZE)) {
        return;
    }
    let uv = (vec2f(id.xy) + 0.5) / SKY_VIEW_LUT_SIZE;
    let unit = vec2f(sub_uv_to_unit(uv.x, SKY_VIEW_LUT_SIZE.x), sub_uv_to_unit(uv.y, SKY_VIEW_LUT_SIZE.y));
    let view_height = length(atmosphere_eye());
    let horizon = sqrt(max(view_height * view_height - BOTTOM_RADIUS * BOTTOM_RADIUS, 0.0));
    let beta = acos(clamp(horizon / view_height, -1.0, 1.0));
    let zenith_horizon_angle = ATMOSPHERE_PI - beta;
    var view_zenith_cos: f32;
    if (unit.y < 0.5) {
        var coordinate = 1.0 - 2.0 * unit.y;
        coordinate = 1.0 - coordinate * coordinate;
        view_zenith_cos = cos(zenith_horizon_angle * coordinate);
    } else {
        let coordinate = unit.y * 2.0 - 1.0;
        view_zenith_cos = cos(zenith_horizon_angle + beta * coordinate * coordinate);
    }
    let light_view_cos = -((unit.x * unit.x) * 2.0 - 1.0);
    // Local frame: the sun lies in the x/z plane; the LUT is symmetric about the sun's azimuth.
    let sun_zenith_cos = sky.sun_direction.z;
    let sun = normalize(vec3f(sqrt(max(1.0 - sun_zenith_cos * sun_zenith_cos, 0.0)), 0.0, sun_zenith_cos));
    let view_zenith_sin = sqrt(max(1.0 - view_zenith_cos * view_zenith_cos, 0.0));
    let direction = vec3f(view_zenith_sin * light_view_cos, view_zenith_sin * sqrt(max(1.0 - light_view_cos * light_view_cos, 0.0)), view_zenith_cos);
    let result = integrate_scattering(transmittance_lut, multi_scattering_lut, lut_sampler, vec3f(0.0, 0.0, view_height), direction, sun, 30.0, 1e9, true, true);
    textureStore(output_2d, id.xy, vec4f(result.luminance, 1.0));
}

var<workgroup> shared_harmonics: array<vec3f, 576>;

// Order-2 spherical harmonics of the sky-view radiance (including the lit ground), convolved with the
// clamped cosine and divided by pi: evaluating them gives irradiance / pi, the Lambert scale for albedo.
@compute @workgroup_size(64, 1, 1)
fn cs_sky_irradiance(@builtin(local_invocation_index) index: u32) {
    var harmonics: array<vec3f, 9>;
    for (var j = 0u; j < 9u; j++) {
        harmonics[j] = vec3f(0.0);
    }
    let total = 1024.0;
    for (var k = 0u; k < 16u; k++) {
        let i = f32(index * 16u + k);
        let z = 1.0 - (2.0 * i + 1.0) / total;
        let r = sqrt(max(1.0 - z * z, 0.0));
        let phi = i * 2.39996323;
        let d = vec3f(r * cos(phi), r * sin(phi), z);
        let radiance = sky_luminance(sky_view_lut, lut_sampler, d);
        harmonics[0] += radiance * 0.282095;
        harmonics[1] += radiance * 0.488603 * d.y;
        harmonics[2] += radiance * 0.488603 * d.z;
        harmonics[3] += radiance * 0.488603 * d.x;
        harmonics[4] += radiance * 1.092548 * d.x * d.y;
        harmonics[5] += radiance * 1.092548 * d.y * d.z;
        harmonics[6] += radiance * 0.315392 * (3.0 * d.z * d.z - 1.0);
        harmonics[7] += radiance * 1.092548 * d.x * d.z;
        harmonics[8] += radiance * 0.546274 * (d.x * d.x - d.y * d.y);
    }
    for (var j = 0u; j < 9u; j++) {
        shared_harmonics[index * 9u + j] = harmonics[j];
    }
    workgroupBarrier();
    if (index == 0u) {
        let weight = 4.0 * ATMOSPHERE_PI / total;
        // Cosine-lobe convolution (pi, 2pi/3, pi/4 per band), then the 1/pi Lambert factor.
        var band = array<f32, 9>(1.0, 2.0 / 3.0, 2.0 / 3.0, 2.0 / 3.0, 0.25, 0.25, 0.25, 0.25, 0.25);
        for (var j = 0u; j < 9u; j++) {
            var sum = vec3f(0.0);
            for (var t = 0u; t < 64u; t++) {
                sum += shared_harmonics[t * 9u + j];
            }
            sky_irradiance[j] = vec4f(sum * weight * band[j], 0.0);
        }
    }
}
