// Drawables' HDR colour to the display, as GTA V's PS_Composite tone-maps its frame: the exposure, then the filmic
// curve (Hable's, with visualsettings.dat's settings) scaled so its white point is one. The game's colour correction,
// shift and desaturation are neutral in clear weather, and its gamma is the sRGB target's. A dither in display space
// follows. The HDR target holds premultiplied colour and coverage, so this is blended over what is already drawn (the
// backdrop) where there is a drawable.
// Scene and the colour helpers come from scene.wgsl, the light from light.wgsl.

@group(1) @binding(0) var hdr_texture: texture_2d<f32>;

@vertex
fn vs_tonemap(@builtin(vertex_index) index: u32) -> @builtin(position) vec4f {
    let corner = vec2f(f32((index << 1u) & 2u), f32(index & 2u));
    return vec4f(corner * 2.0 - 1.0, 0.0, 1.0);
}

// f(x) = (x(Ax + CB) + DE) / (x(Ax + B) + DF) - E/F.
fn filmic(x: vec3f) -> vec3f {
    let a = light.filmic0.x;
    let b = light.filmic0.y;
    let c = light.filmic0.z;
    let d = light.filmic0.w;
    let e = light.filmic1.x;
    let f = light.filmic1.y;
    return (x * (a * x + c * b) + d * e) / (x * (a * x + b) + d * f) - e / f;
}

@fragment
fn fs_tonemap(@builtin(position) frag: vec4f) -> @location(0) vec4f {
    let stored = textureLoad(hdr_texture, vec2i(frag.xy), 0);
    let coverage = stored.a;
    if (coverage <= 0.0) {
        discard;
    }
    let hdr = max(stored.rgb / coverage, vec3f(0.0));
    let exposed = min(hdr, vec3f(30000.0)) * light.sun_direction.w;
    let mapped = saturate(filmic(exposed) * light.filmic1.w);
    // Premultiplied over the backdrop.
    return vec4f(dither(mapped, frag.xy) * coverage, coverage);
}
