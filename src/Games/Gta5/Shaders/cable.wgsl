// GTA V's cables (the cable shader: power lines, telephone wires, tram wires), as VS_Cable and PS_Cable draw them
// (checked against Enhanced's). A cable is stored as a ribbon of no width: each point of its centre line twice, the
// normal along the wire, the first UV's x the radius signed by the side. The vertex shader pushes the two apart, square
// to the wire and the view, so it always faces the camera. Every wire is widened by up to half a unit (a unit is about
// a pixel), so one thinner than a unit is drawn half as wide again as it is, and faded by how thin it is: far wires fade
// out as they thin, as in the game, which has no minimum width. It is forward, blended over what is behind it: lit by
// the ambient alone, natural and artificial, with no sun, shadow or specular. The game sways it in the wind; this shows
// it at rest. The model viewer draws it with vs_cable and fs_cable; a world places it first (Gta5/world_cable.wgsl).
// Scene comes from Scene/scene.wgsl, the light from Scene/light.wgsl.

struct Cable {
    // shader_radiusScale, shader_fadeExponent, shader_cableEmissive, and the alpha test (the game's 0.1).
    options: vec4f,
    // shader_cableDiffuse and shader_cableDiffuse2: the colour, from the first to the second by the vertex alpha.
    diffuse: vec4f,
    diffuse2: vec4f,
    // shader_cableAmbient: the natural and the artificial ambient's scales.
    ambient: vec4f,
};

@group(1) @binding(0) var<uniform> cable: Cable;

struct CableInput {
    @location(0) position: vec3f,
    // The wire's direction.
    @location(1) normal: vec3f,
    // The radius in metres, signed by the side; then how much the wire sways.
    @location(3) uv0: vec2f,
    // The wind's phases in x and y, the colour's blend in alpha.
    @location(5) colour0: vec4f,
};

struct CableOutput {
    @builtin(position) position: vec4f,
    // The colour, and the alpha for how thin the wire is.
    @location(0) colour: vec4f,
    // Across the wire: 0 on one side, 1 on the other.
    @location(1) across: f32,
    // Square to the wire and the view, then square to that and the wire, away from the camera.
    @location(2) side: vec3f,
    @location(3) back: vec3f,
    // Across the wire in units, from minus to plus its half-width, and its half-width.
    @location(4) width: vec2f,
    // Where it is, relative to the scene's origin.
    @location(5) place: vec3f,
};

// gCableParams[0].x, as the game sets it: the target's height over 2.25 tan(fov / 2), so a unit is about 1.1 pixels.
fn cable_scale() -> f32 {
    return scene.viewport.y / (2.25 * scene.projection.y);
}

// A point of the wire, relative to the scene's origin, and the wire's direction there.
fn cable_vertex(p: vec3f, direction: vec3f, uv0: vec2f, colour0: vec4f) -> CableOutput {
    var output: CableOutput;
    let eye = scene.eye.xyz - scene.origin.xyz;
    let radius = cable.options.x * abs(uv0.x);
    let side_sign = select(-1.0, 1.0, uv0.x > 0.0);
    let depth = max(0.01, dot(p - eye, scene.forward.xyz));
    let k = cable_scale() / depth;
    var side = cross(eye - p, direction);
    side = select(vec3f(0.0, 0.0, 1.0), normalize(side), dot(side, side) > 1e-12);
    // The radius in units, widened by up to half a unit.
    let units = radius * k;
    let half_width = units + 0.5 * saturate(units);
    let place = p + side * side_sign * half_width / max(k, 1e-8);
    output.position = scene.view_projection * vec4f(place, 1.0);
    // Thinner than a unit, as faint as it is thin, by shader_fadeExponent (the game raises the thinness squared to half
    // the exponent: gCableParams[0].z is a half).
    let alpha = pow(min(1.0, units), max(1e-8, cable.options.y));
    output.colour = vec4f(mix(cable.diffuse.rgb, cable.diffuse2.rgb, colour0.a), alpha);
    output.across = (side_sign + 1.0) * 0.5;
    output.side = side;
    output.back = cross(side, direction);
    output.width = vec2f(side_sign * half_width, half_width);
    output.place = place;
    return output;
}

@vertex
fn vs_cable(input: CableInput) -> CableOutput {
    // At rest the wind sways nothing.
    return cable_vertex(input.position, input.normal, input.uv0, input.colour0);
}

// The wire's colour and alpha; cut out under the alpha test.
fn cable_colour(input: CableOutput) -> vec4f {
    // Its edges smoothed over their last unit, once the wire is wide enough for it.
    let edge = saturate(input.width.y - abs(input.width.x));
    let alpha = input.colour.a * mix(1.0, edge, saturate(input.width.y * 2.0 / 3.0));
    if (alpha < cable.options.w) {
        discard;
    }
    // The normal turns from the back towards the side across the second half of the ribbon, as on a half cylinder.
    let t = saturate(2.0 * input.across - 1.0);
    var normal = vec3f(0.0, 0.0, 1.0);
    if (dot(input.side, input.side) * dot(input.back, input.back) > 1e-8) {
        normal = normalize(t * normalize(input.side) + sqrt(1.0 - t * t) * normalize(input.back));
    }
    // The natural ambient (with the directional ambient) and the artificial, each by the material's scale squared. Lit
    // as the game does: the ambient by the colour squared, the emissive by the colour.
    let natural = cable.ambient.x * cable.ambient.x;
    let artificial = cable.ambient.y * cable.ambient.y;
    let d = input.colour.rgb;
    let ambient = gta_ambient(normal) * natural + gta_artificial_ambient(normal) * artificial;
    return vec4f((ambient * d + cable.options.z) * d, alpha);
}

@fragment
fn fs_cable(input: CableOutput) -> @location(0) vec4f {
    return cable_colour(input);
}
