// Red Dead Redemption 2's sky tables, as its sky_system effect's compute programs build them (checked against the game's
// compiled programs, their arithmetic kept as it is):
//   - CS_ComputeTransmittance: 128 x 32, the transmittance to the top of the air for a cosine from the zenith (mu =
//     -0.15 + tan(1.5 u) / tan(1.5) * 1.15) and a height ((v)^2 of 60 km); 32 steps, trapezoids; nought into the ground.
//   - CS_ComputeSingleScatter: 256 x 64 x 32, Bruneton's four coordinates in three: across, 8 cosines between the view
//     and the sun (nu) of 32 sun heights (mu_s) each; up, 64 view cosines above the horizon (mu = -c + (1 + c) u^5);
//     deep, 32 heights (16 m + (w)^2 of 59.968 km). The Rayleigh scattering in colour, the Mie in alpha, each over 4 pi.
//   - CS_ComputeGatherLight: 32 x 32 x 32, the light the last order scatters onto a point, gathered around 32 view
//     cosines, by the sun's cosine (as the transmittance's), the height and nu.
//   - CS_ComputeScatteringOrder: the next order, as the single scattering is marched, from what was gathered.
//   - CS_ComputeAccumulate3D: the orders added up, each by the multiple scattering's intensity.
// Put after Rdr2/atmosphere.wgsl.

struct Rdr2Tables {
    atmosphere: Rdr2Atmosphere,
    // How much of each later order is added (sky.MultipleScattering times the timecycle's
    // sky_scattering_multiple_scattering_intensity).
    orders: vec4f,
};

@group(0) @binding(0) var<uniform> tables: Rdr2Tables;
@group(0) @binding(1) var table_sampler: sampler;
@group(0) @binding(2) var transmittance_table: texture_2d<f32>;
@group(0) @binding(3) var volume_in: texture_3d<f32>;
@group(0) @binding(4) var volume_added: texture_3d<f32>;
@group(0) @binding(5) var transmittance_out: texture_storage_2d<rgba16float, write>;
@group(0) @binding(6) var volume_out: texture_storage_3d<rgba16float, write>;

// The extinction a metre at these densities (Rayleigh, aerosols), as the game adds it: the Rayleigh scattering, the
// aerosols' over 0.9 and the ozone with the Rayleigh's density; the fourth the first's.
fn rdr2_extinction(density: vec2f) -> vec4f {
    let a = tables.atmosphere;
    let aerosol = a.rayleigh_mie.w * density.y * 1.111111;
    let rayleigh = vec4f(a.rayleigh_mie.xyz, a.rayleigh_mie.x) * density.x + aerosol + density.x * vec4f(a.ozone.xyz, a.ozone.x);
    return rayleigh;
}

// The densities at a height a metre: exp(-h / H) for the Rayleigh's and the aerosols' scale heights.
fn rdr2_density(height: f32) -> vec2f {
    return exp(-height / tables.atmosphere.heights.yz);
}

@compute @workgroup_size(2, 32)
fn cs_transmittance(@builtin(global_invocation_id) id: vec3u) {
    if (id.x >= 128u || id.y >= 32u) {
        return;
    }
    let v = f32(id.y) * 0.032258;
    let height = clamp(v * v * RDR2_DEPTH, 0.01, 59999.988281);
    let mu = tan(f32(id.x) * 0.011811) * 0.081552 - 0.15;
    let sine = sqrt(max(1.0 - mu * mu, 0.0));
    let r = height + RDR2_GROUND;
    let along = mu * r;
    let a = sine * sine + mu * mu;
    // Into the ground: nothing gets through.
    let ground = (2.0 * along) * (2.0 * along) - a * (r * r - RDR2_GROUND_2) * 4.0;
    let hits = ground >= 0.0 && (-along * 2.0 - sqrt(ground)) / (a + a) >= 0.0;
    var out = vec4f(0.0, 0.0, 0.0, mu);
    if (!hits) {
        let to_top = -r * mu + sqrt(along * along - (r * r - RDR2_TOP_2));
        let ray = to_top * vec2f(sine, mu);
        let length_ = length(ray);
        let step = ray / length_ * (length_ * 0.03125);
        let half_ = length_ * 0.015625;
        var previous = rdr2_density(height);
        var depth = vec2f(0.0);
        for (var i = 1; i <= 32; i++) {
            let p = vec2f(step.x * f32(i), step.y * f32(i) + height + RDR2_GROUND);
            let density = rdr2_density(clamp(length(p) - RDR2_GROUND, 0.0, RDR2_DEPTH));
            depth += (previous + density) * half_;
            previous = density;
        }
        let at = tables.atmosphere;
        let optical = depth.x * at.rayleigh_mie.xyz + depth.y * at.rayleigh_mie.w * 1.111111 + depth.x * at.ozone.xyz;
        out = vec4f(exp(-optical), mu);
    }
    textureStore(transmittance_out, id.xy, out);
}

// A texel of the scattering tables: its height, view cosine (above the horizon), the sun's way in the view's plane and
// the length of its ray, as CS_ComputeSingleScatter and CS_ComputeScatteringOrder work them out. Its ray's step, sine
// and cosine, and whether it reaches the top of the air.
struct Rdr2Texel {
    height: f32,
    r: f32,
    view: vec2f,
    sun: vec2f,
    length_: f32,
    reaches: bool,
};

fn rdr2_texel(id: vec3u) -> Rdr2Texel {
    var texel: Rdr2Texel;
    let w = f32(id.z) * 0.032258;
    let u = f32(id.y) * 0.015873;
    texel.height = 16.0 + w * w * 59968.0;
    texel.r = texel.height + RDR2_GROUND;
    let horizon = sqrt((texel.height + 2.0 * RDR2_GROUND) * texel.height) / texel.r;
    let u5 = u * u * u * u * u;
    let mu = max(0.0001 - horizon, u5 * (horizon + 1.0) - horizon);
    let sine = sqrt(max(1.0 - mu * mu, 0.0));
    texel.view = vec2f(sine, mu);
    let a = sine * sine + mu * mu;
    let along = 2.0 * texel.r * mu;
    let top_discriminant = along * along - 4.0 * a * (texel.r * texel.r - RDR2_TOP_2);
    let to_top = select((-along + sqrt(top_discriminant)) / (a + a), -1.0, top_discriminant < 0.0);
    texel.reaches = to_top > 0.0;
    let ground_discriminant = along * along - 4.0 * a * (texel.r * texel.r - RDR2_GROUND_2);
    let to_ground = select((-along - sqrt(ground_discriminant)) / (a + a), -1.0, ground_discriminant < 0.0);
    texel.length_ = select(to_top, min(to_ground, to_top), to_ground > 0.0);
    // Across: the sun's height of 32, then the cosine to the sun of 8.
    let nu = min(floor(f32(id.x) * 0.03125) * 0.285714 - 1.0, 1.0);
    let mu_s = tan((f32(id.x & 31u) * 0.064516 - 0.74) * 1.1) * 0.186929;
    let across = select(0.0, (nu - mu_s * mu) / sine, sine > 0.00011);
    texel.sun = vec2f(across, mu_s);
    return texel;
}

@compute @workgroup_size(4, 4, 4)
fn cs_single_scatter(@builtin(global_invocation_id) id: vec3u) {
    if (id.x >= 256u || id.y >= 64u || id.z >= 32u) {
        return;
    }
    let texel = rdr2_texel(id);
    var out = vec4f(1.0, 1.0, 1.0, 0.0);
    if (texel.reaches) {
        let at = tables.atmosphere;
        let remap = at.heights.x;
        let step = texel.view * (texel.length_ * 0.03125);
        let half_ = texel.length_ * 0.015625;
        let start = min(texel.height, RDR2_DEPTH);
        let density0 = rdr2_density(start);
        let sun0 = textureSampleLevel(transmittance_table, table_sampler,
            vec2f(rdr2_transmittance_u(texel.sun.y), sqrt(start * remap / RDR2_DEPTH)), 0.0).rgb;
        let lit0 = vec4f(sun0, sun0.x) * exp(-rdr2_extinction(density0)) * vec4f(density0.xxx, density0.y);
        var rayleigh_previous = lit0.xyz;
        var mie_previous = lit0.w;
        var density_previous = density0;
        var depth = vec2f(0.0);
        var rayleigh = vec3f(0.0);
        var mie = 0.0;
        for (var i = 1; i <= 32; i++) {
            let p = vec2f(step.x * f32(i), step.y * f32(i) + texel.height + RDR2_GROUND);
            let distance = length(p);
            let height = clamp(distance - RDR2_GROUND, 0.0, RDR2_DEPTH);
            let density = rdr2_density(height);
            depth += (density_previous + density) * half_;
            let view = exp(-(vec4f(at.rayleigh_mie.xyz, at.rayleigh_mie.x) * depth.x + at.rayleigh_mie.w * 1.111111 * depth.y
                + depth.x * vec4f(at.ozone.xyz, at.ozone.x)));
            let mu_s = dot(texel.sun, p / distance);
            let sun = textureSampleLevel(transmittance_table, table_sampler,
                vec2f(rdr2_transmittance_u(mu_s), sqrt(remap * height / RDR2_DEPTH)), 0.0).rgb;
            let lit = view * vec4f(sun, sun.x);
            mie += (density.y * lit.w + mie_previous) * half_;
            rayleigh += (density.x * lit.xyz + rayleigh_previous) * half_;
            rayleigh_previous = density.x * lit.xyz;
            mie_previous = density.y * lit.w;
            density_previous = density;
        }
        out = vec4f(at.rayleigh_mie.xyz * rayleigh * RDR2_INVERSE_4PI, at.rayleigh_mie.w * mie * RDR2_INVERSE_4PI);
    }
    textureStore(volume_out, id, out);
}

@compute @workgroup_size(4, 4, 4)
fn cs_gather_light(@builtin(global_invocation_id) id: vec3u) {
    if (any(id >= vec3u(32u))) {
        return;
    }
    let v = f32(id.y) * 0.032258;
    let mu_s = tan(f32(id.x) * 0.048387) * 0.081552 - 0.15;
    let nu = f32(id.z) * 0.064516 - 1.0;
    let r = v * v * RDR2_DEPTH * tables.atmosphere.heights.x + RDR2_GROUND;
    let height = abs(r) - RDR2_GROUND;
    let w = sqrt(abs(height) / RDR2_DEPTH);
    let horizon = sqrt(abs(height) * (abs(height) + 2.0 * RDR2_GROUND)) / (abs(height) + RDR2_GROUND);
    let sun_u = rdr2_sun_u(mu_s);
    var sum = vec4f(0.0);
    for (var angle = 0.0; angle < 6.283185; angle += 0.19635) {
        let mu = cos(angle);
        let sine = sqrt(1.0 - mu * mu);
        let across = select(0.0, (nu - mu_s * mu) / sine, sine > 0.00011);
        let up = select(1.0, pow(saturate((mu + horizon) / (horizon + 1.0)), 0.2), -horizon < mu);
        let to_sun = dot(vec2f(sine, mu), vec2f(across, mu_s)) * 0.5 + 0.5;
        let slice = floor(to_sun * 7.0);
        let between = to_sun * 7.0 - slice;
        let a = textureSampleLevel(volume_in, table_sampler, vec3f((sun_u + slice) * 0.125, up, w), 0.0);
        let b = textureSampleLevel(volume_in, table_sampler, vec3f((sun_u + slice + 1.0) * 0.125, up, w), 0.0);
        let scattered = mix(a, b, between);
        sum += vec4f(scattered.xyz, scattered.w * scattered.x / max(scattered.x, 0.0001));
    }
    textureStore(volume_out, id, sum * 0.392699);
}

@compute @workgroup_size(4, 4, 4)
fn cs_scattering_order(@builtin(global_invocation_id) id: vec3u) {
    if (id.x >= 256u || id.y >= 64u || id.z >= 32u) {
        return;
    }
    let texel = rdr2_texel(id);
    var out = vec4f(0.0);
    if (texel.reaches) {
        let at = tables.atmosphere;
        let step = texel.view * (texel.length_ * 0.03125);
        let half_ = texel.length_ * 0.015625;
        let nu = dot(texel.sun, texel.view) * 0.5 + 0.5;
        let gathered0 = textureSampleLevel(volume_in, table_sampler,
            vec3f(rdr2_transmittance_u(texel.sun.y), sqrt(texel.height / RDR2_DEPTH), nu), 0.0);
        let mie_gathered0 = gathered0.w * gathered0.x / max(gathered0.x, 0.0001);
        let density0 = rdr2_density(min(texel.height, RDR2_DEPTH));
        let extinction0 = exp(-rdr2_extinction(density0));
        var rayleigh_previous = density0.x * gathered0.xyz * extinction0.xyz;
        var mie_previous = density0.y * mie_gathered0 * extinction0.x;
        var density_previous = density0;
        var depth = vec2f(0.0);
        var rayleigh = vec3f(0.0);
        var mie = 0.0;
        for (var i = 1; i <= 32; i++) {
            let p = vec2f(step.x * f32(i), step.y * f32(i) + texel.height + RDR2_GROUND);
            let distance = length(p);
            let above = distance - RDR2_GROUND;
            let density = rdr2_density(clamp(above, 0.0, RDR2_DEPTH));
            depth += (density_previous + density) * half_;
            let view = exp(-(at.rayleigh_mie.xyz * depth.x + at.rayleigh_mie.w * 1.111111 * depth.y + depth.x * at.ozone.xyz));
            let mu_s = dot(texel.sun, p / distance);
            let gathered = textureSampleLevel(volume_in, table_sampler,
                vec3f(rdr2_transmittance_u(mu_s), sqrt(abs(above) / RDR2_DEPTH), nu), 0.0);
            let mie_gathered = density.y * gathered.w * gathered.x / max(gathered.x, 0.0001);
            let lit = density.x * gathered.xyz;
            mie += (mie_gathered * view.x + mie_previous) * half_;
            rayleigh += (lit * view + rayleigh_previous) * half_;
            rayleigh_previous = view * lit;
            mie_previous = view.x * mie_gathered;
            density_previous = density;
        }
        out = vec4f(at.rayleigh_mie.xyz * rayleigh * RDR2_INVERSE_4PI, at.rayleigh_mie.w * mie * RDR2_INVERSE_4PI);
    }
    textureStore(volume_out, id, out);
}

@compute @workgroup_size(4, 4, 4)
fn cs_accumulate(@builtin(global_invocation_id) id: vec3u) {
    if (any(id >= textureDimensions(volume_out))) {
        return;
    }
    let total = textureLoad(volume_in, id, 0);
    let added = textureLoad(volume_added, id, 0);
    textureStore(volume_out, id, total + added * tables.orders.x);
}
