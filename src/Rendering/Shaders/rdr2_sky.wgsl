// Red Dead Redemption 2's sky behind its world, as its sky_dome effect draws it by day (vs/ps_sky_normal_none, then
// vs/ps_sun_scattering; checked against the game's compiled programs), from the tables rdr2_sky_tables.wgsl builds:
//   - The scattering by the cosine to the sun, between its 8 slices along a cubic B-spline over four of them; the
//     Rayleigh part lit by the sun's intensity times the sky colour, 3/4 (1 + nu^2) its phase; the Mie part, its colour
//     from the Mie alpha by the Rayleigh's, lit by the sun's intensity times the sun colour, Cornette-Shanks its phase
//     times the Mie intensity; together clamped to the sun's and the moon's intensities.
//   - The sun's disc over it: the transmittance along the view, curved towards the horizon (raised to 1 + (1 - mu)^power
//     (multiplier - 1)), lit by the sun's intensity times the sun colour times the disc's multiplier, its limb darkened,
//     at most 50000, added to the dome.
// The game works them out at the dome's vertices; here, at each pixel, so the frame is the dome. Its radiance is then
// scaled into the light the viewer draws the world with. The night's sky (the moon's, the stars' and the air glow) and
// the dawn's fill aren't drawn yet.
// Put after scene.wgsl and rdr2_atmosphere.wgsl.

@group(1) @binding(0) var<uniform> dome: Rdr2Atmosphere;
@group(1) @binding(1) var dome_sampler: sampler;
@group(1) @binding(2) var dome_transmittance: texture_2d<f32>;
@group(1) @binding(3) var dome_scattering: texture_3d<f32>;

struct Rdr2SkyVarying {
    @builtin(position) position: vec4f,
    @location(0) ndc: vec2f,
};

@vertex
fn vs_rdr2_sky(@builtin(vertex_index) index: u32) -> Rdr2SkyVarying {
    var corners = array<vec2f, 3>(vec2f(-1.0, -1.0), vec2f(3.0, -1.0), vec2f(-1.0, 3.0));
    var out: Rdr2SkyVarying;
    // Reversed Z: at depth nought, behind everything the world draws over it.
    out.position = vec4f(corners[index], 0.0, 1.0);
    out.ndc = corners[index];
    return out;
}

// The scattering towards a view from a height, as vs_sky_normal_none reads it: the Rayleigh part in colour, the Mie in
// alpha.
fn rdr2_scattering(view: vec3f, height: f32) -> vec4f {
    let up = rdr2_view_u(view.z, height);
    let deep = sqrt(height / RDR2_DEPTH);
    let sun_u = rdr2_sun_u(dome.sun.z);
    let to_sun = dot(view, dome.sun.xyz) * 0.5 + 0.5;
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

@fragment
fn fs_rdr2_sky(input: Rdr2SkyVarying) -> @location(0) vec4f {
    let aspect = scene.projection.x;
    let tan_half = scene.projection.y;
    let view = normalize(scene.forward.xyz + scene.right.xyz * input.ndc.x * aspect * tan_half + scene.up.xyz * input.ndc.y * tan_half);
    // The camera's height over the game's ground for the sky: from the altitude bias, either way.
    let height = abs(dome.heights.w - scene.eye.z);
    let scattered = rdr2_scattering(view, height);
    let mie = scattered.w * scattered.rgb / max(scattered.r, 0.0001) * (dome.rayleigh_mie.x / dome.rayleigh_mie.xyz);
    let nu = dot(view, dome.sun.xyz);
    let rayleigh_phase = 0.75 * (1.0 + nu * nu);
    let mie_phase = dome.phase.x * (1.0 + nu * nu) / pow(dome.phase.y * nu + dome.phase.z, 1.5) * dome.phase.w;
    var colour = clamp(scattered.rgb * dome.sun_sky.rgb * rayleigh_phase + mie * dome.sun_sun.rgb * mie_phase, vec3f(0.0),
        vec3f(dome.sun_sky.w));

    // The sun's disc, where the view is within its radius.
    let tangent = sqrt(max(1.0 - nu * nu, 0.0)) / max(nu, 0.0001);
    let across = tangent / dome.sun.w;
    if (nu > 0.0 && across < 1.0) {
        let transmittance = textureSampleLevel(dome_transmittance, dome_sampler,
            vec2f(rdr2_transmittance_u(view.z), sqrt(min(height, RDR2_DEPTH) * dome.heights.x / RDR2_DEPTH)), 0.0).rgb;
        let curve = 1.0 + pow(1.0 - saturate(view.z), dome.disc.y) * (dome.disc.x - 1.0);
        let limb = saturate(1.0 - dome.disc.z * (1.0 - sqrt(1.0 - across * across)));
        let disc = min(pow(transmittance, vec3f(curve)) * limb * dome.sun_sun.rgb * dome.sun_sun.w, vec3f(50000.0));
        // Added to the dome behind it.
        colour += disc;
    }
    return vec4f(colour * dome.disc.w, 1.0);
}
