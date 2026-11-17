// GTA V's fog over its world, as the game's depth effects pass draws it (the postfx effect's pp_depthfx, PS_DepthEffects; the
// same arithmetic in Enhanced's program): once the world's solid surfaces and decals are drawn, a triangle over the
// frame takes each pixel's depth back to its point and blends the fog along the ray to it (gta_fog.wgsl, with the haze)
// over it, by the fog's amount, into colour only. The sky, which nothing covers, fogs itself (gta_sky.wgsl); the water
// and blended surfaces drawn after this fog themselves (gta_water.wgsl, the depth effects in gta_world.wgsl and
// gta_world_terrain.wgsl), the water by its own surface, as the game's pass would over it.
//   - The game skips what's nearer than the fog's start with a depth test against a plane there; nothing there has any
//     fog, so here it's simply drawn over.
// Put after scene.wgsl, light.wgsl and gta_fog.wgsl.

// The world's depth (reversed Z, infinitely far), read where it's attached.
@group(1) @binding(0) var fog_depth: texture_depth_2d;

@vertex
fn vs_gta_world_fog(@builtin(vertex_index) index: u32) -> @builtin(position) vec4f {
    let corner = vec2f(f32((index << 1u) & 2u), f32(index & 2u));
    return vec4f(corner * 2.0 - 1.0, 0.0, 1.0);
}

@fragment
fn fs_gta_world_fog(@builtin(position) position: vec4f) -> @location(0) vec4f {
    let depth = textureLoad(fog_depth, vec2i(position.xy), 0);
    // The sky: nothing was drawn there.
    if (depth == 0.0) {
        discard;
    }
    // The pixel's point: along its ray, as far along the view as the depth says (the near plane over it).
    let size = vec2f(textureDimensions(fog_depth));
    let ndc = vec2f(position.x / size.x * 2.0 - 1.0, 1.0 - position.y / size.y * 2.0);
    let tan_half = scene.projection.y;
    let ray = scene.forward.xyz + scene.right.xyz * (ndc.x * tan_half * scene.projection.x) + scene.up.xyz * (ndc.y * tan_half);
    let fog = gta_fog(world_fog, ray * (light.water.z / depth), 1.0);
    return fog;
}
