// Red Dead Redemption 2's aerial perspective over its world, as its sky system works it out (CS_ComputeAerialLookUp;
// checked against the game's compiled programs) and lays it over what's drawn (PS_ApplyAerialTransmittance):
//   - Over a 32 by 32 grid of the frame, each cell's ray marched out to the far clip in 32 slices, as far apart as each
//     other: at each, the view's transmittance, and the sun's and the moon's light through the air to it (the
//     transmittance table's) plus the light gathered there from every order of the scattering, by the air's Rayleigh
//     and aerosol densities, added up along the ray (trapezoids, as the game's). The game keeps the in-scattered light
//     apart, sun and moon, Rayleigh and Mie, its colours squeezed; here it's put together as the dome lights it, by the
//     phases for the cell's way and the sun's and moon's light, as the cell's way is the same at every slice.
//   - The height fog, as the game's local atmosphere fogs its world (CS_GatherMaterialVolume's global fog, lit by
//     CS_GatherScatteringVolume and added up by CS_AccumulateScatteringVolume): over a 64 by 36 grid of the frame, each
//     cell's ray in 64 slices out to the far clip, finer near the camera; at each, the fog's density, its timecycle
//     density times (1 - saturate((height - its height) / its falloff))^3, lighting it with the key light through
//     Henyey-Greenstein's phase by its anisotropy and the sky's ambient (its ambient cube's mean) by their tints, plus its
//     fill ambient; each slice's light added as (S - S T) / density times the transmittance before it.
//   - Over the frame, where something's drawn: its colour times the transmittance from the camera to it, then the light
//     scattered in on the way, from the slices its distance falls at: the fog's in front of the air's.
// In the game's own units, as its world and sky are drawn in. Put after scene.wgsl, rdr2_atmosphere.wgsl and
// rdr2_sky.wgsl.

@group(2) @binding(0) var aerial_transmittance_out: texture_storage_3d<rgba16float, write>;
@group(2) @binding(1) var aerial_inscatter_out: texture_storage_3d<rgba16float, write>;
@group(2) @binding(2) var fog_transmittance_out: texture_storage_3d<rgba16float, write>;
@group(2) @binding(3) var fog_inscatter_out: texture_storage_3d<rgba16float, write>;

const RDR2_AERIAL_SIZE: u32 = 32u;
const RDR2_FOG_GRID: vec3u = vec3u(64u, 36u, 64u);
// The fog's slices: the distance at a fraction t of the way out is RDR2_FOG_NEAR ((1 + far / RDR2_FOG_NEAR)^t - 1).
const RDR2_FOG_NEAR: f32 = 32.0;

fn rdr2_fog_distance(t: f32, far: f32) -> f32 {
    return RDR2_FOG_NEAR * (pow(1.0 + far / RDR2_FOG_NEAR, t) - 1.0);
}

fn rdr2_fog_fraction(distance: f32, far: f32) -> f32 {
    return log(1.0 + distance / RDR2_FOG_NEAR) / log(1.0 + far / RDR2_FOG_NEAR);
}

// The fog's density a metre at a height in the world.
fn rdr2_fog_density(height: f32) -> f32 {
    let fog = dome.night.fog[0];
    let thinned = 1.0 - saturate((height - fog.y) * fog.z);
    return fog.x * thinned * thinned * thinned;
}

// Henyey-Greenstein's phase, as the game's: (1 - g^2) / (1 + g^2 - 2 g cos)^1.5 over 4 pi.
fn rdr2_fog_phase(cosine: f32, g: f32) -> f32 {
    return (1.0 - g * g) / pow(1.0 + g * g - 2.0 * g * cosine, 1.5) * RDR2_INVERSE_4PI;
}

@compute @workgroup_size(8, 8)
fn cs_rdr2_fog(@builtin(global_invocation_id) id: vec3u) {
    if (id.x >= RDR2_FOG_GRID.x || id.y >= RDR2_FOG_GRID.y) {
        return;
    }
    let night = dome.night;
    let fog = night.fog;
    let across = f32(id.x) * 2.0 / f32(RDR2_FOG_GRID.x - 1u) - 1.0;
    let down = 1.0 - f32(id.y) * 2.0 / f32(RDR2_FOG_GRID.y - 1u);
    let view = normalize(night.rays[0].xyz + night.rays[1].xyz * across + night.rays[2].xyz * down);
    let far = night.rays[0].w;
    // The key light (the sky's, as the world's lit by it) and the sky's ambient, its cube's mean.
    let toward = select(night.moon.xyz, dome.atmosphere.sun.xyz, night.key_light.w > 0.5);
    let key = sky_light[0].rgb * fog[1].rgb * rdr2_fog_phase(dot(view, toward), fog[0].w);
    var ambient = vec3f(0.0);
    for (var face = 1; face < 7; face++) {
        ambient += sky_light[face].rgb;
    }
    let lit = key + ambient / 6.0 * fog[2].rgb + fog[3].rgb;
    var transmittance = 1.0;
    var inscattered = vec3f(0.0);
    var behind = 0.0;
    for (var slice = 0u; slice < RDR2_FOG_GRID.z; slice++) {
        let ahead = rdr2_fog_distance((f32(slice) + 1.0) / f32(RDR2_FOG_GRID.z), far);
        let middle = (behind + ahead) * 0.5;
        let density = rdr2_fog_density(night.camera.z + view.z * middle);
        let through = exp(-density * (ahead - behind));
        // (S - S T) / density, S the light scattered in a metre: density times what lights it.
        inscattered += lit * (1.0 - through) * transmittance;
        transmittance *= through;
        behind = ahead;
        let cell = vec3u(id.xy, slice);
        textureStore(fog_transmittance_out, cell, vec4f(vec3f(transmittance), 1.0));
        textureStore(fog_inscatter_out, cell, vec4f(inscattered, 1.0));
    }
}

// The densities (Rayleigh, aerosols) at a place above the planet's middle.
fn rdr2_aerial_density(place: vec3f) -> vec2f {
    let height = clamp(length(place + vec3f(0.0, 0.0, RDR2_GROUND)) - RDR2_GROUND, 0.0, RDR2_DEPTH);
    return exp(-height / dome.atmosphere.heights.yz);
}

// The extinction of optical depths (Rayleigh, aerosols): the Rayleigh scattering, the aerosols' over 0.9, the ozone.
fn rdr2_aerial_extinction(depth: vec2f) -> vec3f {
    let at = dome.atmosphere;
    return at.rayleigh_mie.xyz * depth.x + at.rayleigh_mie.w * 1.111111 * depth.y + at.ozone.xyz * depth.x;
}

// What a point lit by a light from a way sends on along the view, per unit of each density: the light's transmittance
// to it, and what's gathered there from every order (the Rayleigh's, then the Mie's from its alpha).
struct Rdr2AerialLight {
    rayleigh: vec3f,
    mie: vec3f,
};

fn rdr2_aerial_light(place: vec3f, toward: vec3f, view: vec3f) -> Rdr2AerialLight {
    let at = dome.atmosphere;
    let outward = place + vec3f(0.0, 0.0, RDR2_GROUND);
    let distance = length(outward);
    let height = clamp(distance - RDR2_GROUND, 0.0, RDR2_DEPTH);
    let up = outward / distance;
    let mu_s = dot(toward, up);
    let transmittance = textureSampleLevel(dome_transmittance, dome_sampler,
        vec2f(rdr2_transmittance_u(mu_s), sqrt(height * at.heights.x / RDR2_DEPTH)), 0.0).rgb;
    let gathered = textureSampleLevel(dome_gathered, dome_sampler,
        vec3f(rdr2_transmittance_u(mu_s), sqrt(height / RDR2_DEPTH), dot(toward, view) * 0.5 + 0.5), 0.0);
    let factor = dome.orders.x;
    var light: Rdr2AerialLight;
    light.rayleigh = transmittance + gathered.rgb * factor;
    light.mie = transmittance + rdr2_mie(gathered) * factor;
    return light;
}

@compute @workgroup_size(8, 8)
fn cs_rdr2_aerial(@builtin(global_invocation_id) id: vec3u) {
    if (id.x >= RDR2_AERIAL_SIZE || id.y >= RDR2_AERIAL_SIZE) {
        return;
    }
    let night = dome.night;
    let at = dome.atmosphere;
    let across = f32(id.x) * 2.0 / f32(RDR2_AERIAL_SIZE - 1u) - 1.0;
    let down = 1.0 - f32(id.y) * 2.0 / f32(RDR2_AERIAL_SIZE - 1u);
    let view = normalize(night.rays[0].xyz + night.rays[1].xyz * across + night.rays[2].xyz * down);
    let far = night.rays[0].w;
    let moon = night.moon.xyz;
    let moon_drawn = night.flags.y > 0.5;
    // The camera above the game's ground, from the altitude bias.
    let start = vec3f(0.0, 0.0, night.camera.z - at.heights.w);
    var density = rdr2_aerial_density(start);
    var view_transmittance = vec3f(1.0);
    var sun_light = rdr2_aerial_light(start, at.sun.xyz, view);
    var moon_light = rdr2_aerial_light(start, moon, view);
    var sun_rayleigh = density.x * sun_light.rayleigh;
    var sun_mie = density.y * sun_light.mie;
    var moon_rayleigh = density.x * moon_light.rayleigh;
    var moon_mie = density.y * moon_light.mie;
    var sums: array<vec3f, 4>;
    for (var i = 0; i < 4; i++) {
        sums[i] = vec3f(0.0);
    }
    var depth = vec2f(0.0);
    var previous = start;
    let phases = rdr2_phases(dot(view, at.sun.xyz));
    let moon_phases = rdr2_phases(dot(view, moon));
    for (var slice = 0u; slice < RDR2_AERIAL_SIZE; slice++) {
        let place = start + view * ((f32(slice) + 0.5) / f32(RDR2_AERIAL_SIZE) * far);
        let step = length(place - previous);
        let here = rdr2_aerial_density(place);
        depth += (density + here) * 0.5 * step;
        view_transmittance = exp(-rdr2_aerial_extinction(depth));
        sun_light = rdr2_aerial_light(place, at.sun.xyz, view);
        moon_light = rdr2_aerial_light(place, moon, view);
        let next = array<vec3f, 4>(here.x * view_transmittance * sun_light.rayleigh, here.y * view_transmittance * sun_light.mie,
            here.x * view_transmittance * moon_light.rayleigh, here.y * view_transmittance * moon_light.mie);
        sums[0] += (sun_rayleigh + next[0]) * 0.5 * step;
        sums[1] += (sun_mie + next[1]) * 0.5 * step;
        sums[2] += (moon_rayleigh + next[2]) * 0.5 * step;
        sums[3] += (moon_mie + next[3]) * 0.5 * step;
        sun_rayleigh = next[0];
        sun_mie = next[1];
        moon_rayleigh = next[2];
        moon_mie = next[3];
        density = here;
        previous = place;
        // The light scattered in, over 4 pi, lit as the dome lights it.
        var inscattered = at.rayleigh_mie.xyz * sums[0] * RDR2_INVERSE_4PI * at.sun_sky.rgb * phases.x
            + at.rayleigh_mie.w * sums[1] * RDR2_INVERSE_4PI * at.sun_sun.rgb * phases.y;
        if (moon_drawn) {
            inscattered += at.rayleigh_mie.xyz * sums[2] * RDR2_INVERSE_4PI * night.moon_sky.rgb * moon_phases.x
                + at.rayleigh_mie.w * sums[3] * RDR2_INVERSE_4PI * night.moon_sun.rgb * moon_phases.y;
        }
        let cell = vec3u(id.xy, slice);
        textureStore(aerial_transmittance_out, cell, vec4f(view_transmittance, 1.0));
        textureStore(aerial_inscatter_out, cell, vec4f(inscattered, 1.0));
    }
}

@group(1) @binding(10) var aerial_depth: texture_depth_2d;
@group(1) @binding(11) var aerial_transmittance: texture_3d<f32>;
@group(1) @binding(12) var aerial_inscatter: texture_3d<f32>;
@group(1) @binding(13) var fog_transmittance: texture_3d<f32>;
@group(1) @binding(14) var fog_inscatter: texture_3d<f32>;

struct Rdr2AerialVarying {
    @builtin(position) position: vec4f,
    @location(0) ndc: vec2f,
};

@vertex
fn vs_rdr2_aerial(@builtin(vertex_index) index: u32) -> Rdr2AerialVarying {
    var corners = array<vec2f, 3>(vec2f(-1.0, -1.0), vec2f(3.0, -1.0), vec2f(-1.0, 3.0));
    var out: Rdr2AerialVarying;
    out.position = vec4f(corners[index], 0.0, 1.0);
    out.ndc = corners[index];
    return out;
}

// Where in the aerial perspective a pixel's surface falls: its cell's coordinates and the slice of its distance over
// the far clip; none for the sky (nothing drawn there). And in the fog's grid.
struct Rdr2AerialCell {
    aerial: vec3f,
    fog: vec3f,
    drawn: f32,
};

fn rdr2_aerial_cells(input: Rdr2AerialVarying) -> Rdr2AerialCell {
    let night = dome.night;
    let depth = textureLoad(aerial_depth, vec2i(input.position.xy), 0);
    let along = night.rays[2].w / max(depth, 1e-12);
    let ray = night.rays[0].xyz + night.rays[1].xyz * input.ndc.x + night.rays[2].xyz * input.ndc.y;
    let distance = along * length(ray);
    let far = night.rays[0].w;
    var cell: Rdr2AerialCell;
    let size = f32(RDR2_AERIAL_SIZE);
    cell.aerial = vec3f((vec2f(input.ndc.x * 0.5 + 0.5, 0.5 - input.ndc.y * 0.5) * (size - 1.0) + 0.5) / size, saturate(distance / far));
    let grid = vec2f(RDR2_FOG_GRID.xy);
    // Each slice holds what's in front of its far end.
    let slices = f32(RDR2_FOG_GRID.z);
    let slice = (saturate(rdr2_fog_fraction(distance, far)) * slices - 0.5) / slices;
    cell.fog = vec3f((vec2f(input.ndc.x * 0.5 + 0.5, 0.5 - input.ndc.y * 0.5) * (grid - 1.0) + 0.5) / grid, slice);
    cell.drawn = select(1.0, 0.0, depth <= 0.0);
    return cell;
}

// The colour behind times the transmittance from the camera, through the fog and the air.
@fragment
fn fs_rdr2_aerial_transmittance(input: Rdr2AerialVarying) -> @location(0) vec4f {
    let cell = rdr2_aerial_cells(input);
    let air = textureSampleLevel(aerial_transmittance, dome_sampler, cell.aerial, 0.0).rgb;
    let fog = textureSampleLevel(fog_transmittance, dome_sampler, cell.fog, 0.0).rgb;
    return vec4f(mix(vec3f(1.0), air * fog, cell.drawn), 1.0);
}

// Then the light scattered in on the way: the fog's, and the air's through it.
@fragment
fn fs_rdr2_aerial_inscatter(input: Rdr2AerialVarying) -> @location(0) vec4f {
    let cell = rdr2_aerial_cells(input);
    let air = textureSampleLevel(aerial_inscatter, dome_sampler, cell.aerial, 0.0).rgb;
    let fog = textureSampleLevel(fog_inscatter, dome_sampler, cell.fog, 0.0).rgb;
    let through = textureSampleLevel(fog_transmittance, dome_sampler, cell.fog, 0.0).rgb;
    return vec4f((fog + air * through) * cell.drawn, 1.0);
}
