using System.Numerics;
using HikariHopper.Rendering;

namespace HikariHopper.Tests;

// The world's GPU side, without a game: every game's pipelines and a frame of it, with its shadow, raise no GPU errors.
public sealed class WorldRenderingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_worlds_pipelines_compile_and_its_frames_raise_no_GPU_errors(bool direct)
    {
        ViewportRenderer.DirectWorldDraws = direct;
        GpuDevice gpu = GpuDevice.Shared;
        lock (gpu.Gate)
        {
            var renderer = new ViewportRenderer(gpu);
            WebGpuSharp.Texture target = gpu.Device.CreateTexture(new WebGpuSharp.TextureDescriptor
            {
                Usage = WebGpuSharp.TextureUsage.RenderAttachment,
                Dimension = WebGpuSharp.TextureDimension.D2,
                Size = new WebGpuSharp.Extent3D(64, 32, 1),
                Format = renderer.PresentFormat,
                MipLevelCount = 1,
                SampleCount = 1,
            });
            gpu.Device.PushErrorScope(WebGpuSharp.ErrorFilter.Validation);
            renderer.CreateWorldPipelines();
            renderer.CreatePickPipelines();
            // Each game's: RDR2's material, and GTA V's surfaces and terrain, drawn, cast and picked.
            foreach (WorldFamily family in Enum.GetValues<WorldFamily>())
            {
                renderer.CreateWorldFamily(family);
                renderer.CreatePickFamily(family);
            }
            renderer.Resize(64, 32);
            renderer.WorldShown = true;
            renderer.Shadows = true;
            renderer.Camera.Frame(new Vector3(-300, 740, 132), 20);
            renderer.Camera.Free = true;
            renderer.Camera.Place(new Vector3(-300, 740, 132), -MathF.PI / 2, .18f);
            renderer.SetTime(14);
            renderer.RenderTo(target.CreateView());
            // Nothing streamed: nothing under the pointer.
            Assert.Null(renderer.PickWorldAt(32, 16));
            var (type, message) = gpu.Device.PopErrorScopeSync(5_000_000_000);
            Assert.True(type == WebGpuSharp.ErrorType.NoError, message);
            Assert.Equal(WorldShadowCount, renderer.CascadesCast);
            renderer.Dispose();
            target.Destroy();
        }
        ViewportRenderer.DirectWorldDraws = false;
    }

    [Fact]
    public void A_texture_read_again_for_another_asset_is_the_one_on_the_GPU_and_its_pixels_go()
    {
        GpuDevice gpu = TestGpu.Device();
        lock (gpu.Gate)
        {
            var renderer = new ViewportRenderer(gpu);
            gpu.Device.PushErrorScope(WebGpuSharp.ErrorFilter.Validation);
            var weak = new List<WeakReference>();
            // Two assets reading the same dictionary's texture, each its own read of it: different objects, one texture.
            void Add(uint hash)
            {
                var dictionary = new FiveFury.Textures.TextureDictionary();
                var texture = new FiveFury.Textures.Texture("road_d", 4, 4, CommonFury.Textures.TextureFormat.BC1Unorm, 1, new byte[8]);
                dictionary.Textures.Add(texture);
                weak.Add(new WeakReference(texture));
                FiveFury.Drawables.Drawable drawable = FiveFury.Ydr.Ydr.Read(TestDrawables.Triangle(diffuseName: "road_d")).Drawable;
                DrawableScene scene = DrawableScene.Build(drawable, [dictionary]);
                Assert.Same(texture, scene.Materials.Single().Diffuse);
                var asset = Rendering.World.WorldAsset.FromGta(new RedFury.World.WorldAssetKey(hash, 2), scene, [drawable],
                    read => new Rendering.World.WorldTextureId(7, 9));
                Assert.True(renderer.AddWorldAsset(asset, new HashSet<RedFury.World.WorldAssetKey>(), long.MaxValue));
                while (renderer.HasPendingWorldUpload)
                    renderer.ProcessWorldUploads();
            }
            Add(1);
            Add(2);
            Assert.True(renderer.IsWorldReady(new RedFury.World.WorldAssetKey(1, 2)) && renderer.IsWorldReady(new RedFury.World.WorldAssetKey(2, 2)));
            Assert.Equal(1, renderer.WorldTextureCount);
            // Up, nothing holds the textures read (nor their pixels).
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.All(weak, reference => Assert.False(reference.IsAlive));
            var (type, message) = gpu.Device.PopErrorScopeSync(5_000_000_000);
            Assert.True(type == WebGpuSharp.ErrorType.NoError, message);
            renderer.ClearWorld();
            Assert.Equal(0, renderer.WorldTextureCount);
            renderer.Dispose();
        }
    }

    [Fact]
    public void A_model_s_own_texture_read_again_is_the_one_on_the_GPU_and_its_pixels_go()
    {
        GpuDevice gpu = TestGpu.Device();
        lock (gpu.Gate)
        {
            var renderer = new ViewportRenderer(gpu);
            gpu.Device.PushErrorScope(WebGpuSharp.ErrorFilter.Validation);
            var weak = new List<WeakReference>();
            byte[] ydr = TestDrawables.Triangle(diffuse: Textures.TextureTestKit.Bgra("own_d", 4, 4, [10, 20, 30, 255]));
            // Two assets with the same texture of their own, which no dictionary names, each its own read of it: different
            // objects, the same pixels, one texture.
            void Add(uint hash)
            {
                FiveFury.Drawables.Drawable drawable = FiveFury.Ydr.Ydr.Read(ydr).Drawable;
                DrawableScene scene = DrawableScene.Build(drawable, []);
                weak.Add(new WeakReference(scene.Materials.Single().Diffuse ?? throw new InvalidOperationException("no texture")));
                var asset = Rendering.World.WorldAsset.FromGta(new RedFury.World.WorldAssetKey(hash, 2), scene, [drawable]);
                Assert.True(renderer.AddWorldAsset(asset, new HashSet<RedFury.World.WorldAssetKey>(), long.MaxValue));
                while (renderer.HasPendingWorldUpload)
                    renderer.ProcessWorldUploads();
            }
            Add(1);
            Add(2);
            Assert.True(renderer.IsWorldReady(new RedFury.World.WorldAssetKey(1, 2)) && renderer.IsWorldReady(new RedFury.World.WorldAssetKey(2, 2)));
            Assert.Equal(1, renderer.WorldTextureCount);
            // Up, nothing holds the textures read (nor their pixels).
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.All(weak, reference => Assert.False(reference.IsAlive));
            var (type, message) = gpu.Device.PopErrorScopeSync(5_000_000_000);
            Assert.True(type == WebGpuSharp.ErrorType.NoError, message);
            renderer.ClearWorld();
            Assert.Equal(0, renderer.WorldTextureCount);
            renderer.Dispose();
        }
    }

    [Fact]
    public void An_asset_is_charged_what_it_keeps_on_the_GPU_and_a_texture_two_bind_once()
    {
        byte[] ydr = TestDrawables.Triangle(diffuse: Textures.TextureTestKit.Bgra("own_d", 16, 16, [10, 20, 30, 255]));
        Rendering.World.WorldAsset Read(uint hash)
        {
            FiveFury.Drawables.Drawable drawable = FiveFury.Ydr.Ydr.Read(ydr).Drawable;
            return Rendering.World.WorldAsset.FromGta(new RedFury.World.WorldAssetKey(hash, 2), DrawableScene.Build(drawable, []), [drawable]);
        }
        Rendering.World.WorldAsset first = Read(1), second = Read(2);
        long pixels = first.Scene.Materials.Single().Diffuse!.Data.Length;
        // Its vertices and indexes once, its material's values, and its texture's pixels once: not what reading it took.
        long geometry = (long)first.Layout.Vertices.Length * SurfaceVertex.Size + first.Layout.Indices.Length * sizeof(uint);
        Assert.Equal(geometry + Math.Max(Rdr2Material.UniformBytes, SurfaceMaterial.UniformBytes) + 1024 + first.LightBones.Bytes, first.MemoryCost.OwnedBytes);
        Assert.Equal([pixels], first.MemoryCost.Shared.Select(shared => shared.Bytes));
        Assert.Equal(first.MemoryCost.OwnedBytes + pixels, first.Bytes);
        // The two read the same texture: it's one allocation, counted once between them.
        Assert.Equal(first.MemoryCost.Shared.Single().Identity, second.MemoryCost.Shared.Single().Identity);
        Assert.Equal(first.MemoryCost.OwnedBytes + second.MemoryCost.OwnedBytes + pixels,
            RedFury.World.WorldMemoryCost.Total([first.MemoryCost, second.MemoryCost]));
    }

    [Fact]
    public void A_model_whose_shadow_proxy_is_only_in_its_first_level_casts_it_past_that_level_s_reach()
    {
        GpuDevice gpu = TestGpu.Device();
        lock (gpu.Gate)
        {
            var renderer = new ViewportRenderer(gpu);
            WebGpuSharp.Texture target = gpu.Device.CreateTexture(new WebGpuSharp.TextureDescriptor
            {
                Usage = WebGpuSharp.TextureUsage.RenderAttachment,
                Dimension = WebGpuSharp.TextureDimension.D2,
                Size = new WebGpuSharp.Extent3D(64, 32, 1),
                Format = renderer.PresentFormat,
                MipLevelCount = 1,
                SampleCount = 1,
            });
            WebGpuSharp.TextureView view = target.CreateView();
            renderer.Resize(64, 32);
            renderer.WorldShown = true;
            renderer.Shadows = true;
            renderer.SetTime(14);
            renderer.Camera.Frame(new Vector3(0, -50, 1), 20);
            renderer.Camera.Free = true;
            renderer.Camera.Place(new Vector3(0, -50, 1), -MathF.PI / 2, 0);
            gpu.Device.PushErrorScope(WebGpuSharp.ErrorFilter.Validation);
            // A tree as the game's are made: its first level, to 10 m, drawn and cast; its second drawn only.
            var builder = new FiveFury.Drawables.DrawableBuilder { Name = "tree" };
            builder.Materials.Add(new FiveFury.Drawables.DrawableMaterialInput { Name = "surface", Shader = "default" });
            foreach (var (lod, mask) in new[]
            {
                (CommonFury.Assets.DrawableLod.High, FiveFury.Drawables.DrawableRenderMask.Default | FiveFury.Drawables.DrawableRenderMask.Shadow),
                (CommonFury.Assets.DrawableLod.Medium, FiveFury.Drawables.DrawableRenderMask.Default),
            })
            {
                var model = new FiveFury.Drawables.DrawableModelInput { Lod = lod, RenderMask = mask };
                model.Meshes.Add(new FiveFury.Drawables.DrawableMeshInput([new(0, 0, 0), new(1, 0, 0), new(0, 0, 1)], [0, 1, 2], "surface"));
                builder.Models.Add(model);
            }
            builder.LodDistances[CommonFury.Assets.DrawableLod.High] = 10;
            FiveFury.Drawables.Drawable drawable = FiveFury.Ydr.Ydr.Read(
                new FiveFury.Ydr.Ydr(builder.Build(FiveFury.GameTarget.Legacy), FiveFury.GameTarget.Legacy).Write()).Drawable;
            var key = new RedFury.World.WorldAssetKey(1, 2);
            var asset = Rendering.World.WorldAsset.FromGta(key, DrawableScene.Build(drawable, []), [drawable]);
            Assert.Equal((0b11, 0b01), (asset.LodMask, asset.CasterLods));
            Assert.True(renderer.AddWorldAsset(asset, new HashSet<RedFury.World.WorldAssetKey>(), long.MaxValue));
            while (renderer.HasPendingWorldUpload)
                renderer.ProcessWorldUploads();
            // 50 m from the camera: in view at its second level, which casts nothing.
            var node = new RedFury.World.WorldLodNode(new RedFury.World.WorldEntityId(1, 0), null, key, Matrix4x4.Identity, new Vector3(.5f, 0, .5f), 1, 1000, 0, 0);
            renderer.SetWorldSelection(new RedFury.World.WorldSelection([node], [key]));
            renderer.RenderTo(view);
            Assert.Equal(WorldShadowCount, renderer.CascadesCast);
            Assert.Equal((0, 1), (renderer.WorldPlacementsAt(key, 0).View, renderer.WorldPlacementsAt(key, 1).View));
            // Its shadow is its first level's, in every cascade it reaches; nothing is cast of the level in view.
            Assert.True(renderer.WorldPlacementsAt(key, 0).Cast > 0, "The model cast no shadow past its first level's reach.");
            Assert.Equal(0, renderer.WorldPlacementsAt(key, 1).Cast);
            var (type, message) = gpu.Device.PopErrorScopeSync(5_000_000_000);
            Assert.True(type == WebGpuSharp.ErrorType.NoError, message);
            renderer.Dispose();
            target.Destroy();
        }
    }

    [Fact]
    public void A_moving_camera_s_frame_is_drawn_from_where_the_camera_is_now()
    {
        GpuDevice gpu = TestGpu.Device();
        lock (gpu.Gate)
        {
            var renderer = new ViewportRenderer(gpu);
            WebGpuSharp.Texture target = gpu.Device.CreateTexture(new WebGpuSharp.TextureDescriptor
            {
                Usage = WebGpuSharp.TextureUsage.RenderAttachment,
                Dimension = WebGpuSharp.TextureDimension.D2,
                Size = new WebGpuSharp.Extent3D(64, 32, 1),
                Format = renderer.PresentFormat,
                MipLevelCount = 1,
                SampleCount = 1,
            });
            WebGpuSharp.TextureView view = target.CreateView();
            renderer.Resize(64, 32);
            renderer.WorldShown = true;
            renderer.Camera.Frame(new Vector3(195, -1030, 62), 20);
            renderer.Camera.Free = true;
            renderer.Camera.Place(new Vector3(195, -1030, 62), -MathF.PI / 2, .2f);
            renderer.RenderTo(view);
            // Flown 30 m sideways between frames: the point 10 m ahead of the camera now is in the middle of this frame, its
            // origin the camera, not where it was the frame before.
            renderer.Camera.Fly(0, 1, 0, 1);
            renderer.RenderTo(view);
            var (matrix, origin) = renderer.SceneView;
            Assert.Equal(renderer.Camera.Eye, origin);
            Vector4 ahead = Vector4.Transform(new Vector4(renderer.Camera.Eye + renderer.Camera.Forward * 10 - origin, 1), matrix);
            Assert.Equal(0, ahead.X / ahead.W, 3);
            Assert.Equal(0, ahead.Y / ahead.W, 3);
            renderer.Dispose();
            target.Destroy();
        }
    }

    [Fact]
    public void A_placement_leaving_the_coverage_fades_out_while_the_one_replacing_it_fades_in()
    {
        GpuDevice gpu = TestGpu.Device();
        lock (gpu.Gate)
        {
            var renderer = new ViewportRenderer(gpu);
            WebGpuSharp.Texture target = gpu.Device.CreateTexture(new WebGpuSharp.TextureDescriptor
            {
                Usage = WebGpuSharp.TextureUsage.RenderAttachment,
                Dimension = WebGpuSharp.TextureDimension.D2,
                Size = new WebGpuSharp.Extent3D(64, 32, 1),
                Format = renderer.PresentFormat,
                MipLevelCount = 1,
                SampleCount = 1,
            });
            WebGpuSharp.TextureView view = target.CreateView();
            renderer.Resize(64, 32);
            renderer.WorldShown = true;
            renderer.Camera.Frame(new Vector3(0, -5, 1), 20);
            renderer.Camera.Free = true;
            renderer.Camera.Place(new Vector3(0, -5, 1), -MathF.PI / 2, 0);
            gpu.Device.PushErrorScope(WebGpuSharp.ErrorFilter.Validation);
            // A low-detail model and the one replacing it, both up.
            RedFury.World.WorldLodNode Place(uint hash, int index)
            {
                FiveFury.Drawables.Drawable drawable = FiveFury.Ydr.Ydr.Read(TestDrawables.Triangle()).Drawable;
                var key = new RedFury.World.WorldAssetKey(hash, 2);
                Assert.True(renderer.AddWorldAsset(Rendering.World.WorldAsset.FromGta(key, DrawableScene.Build(drawable, []), [drawable]),
                    new HashSet<RedFury.World.WorldAssetKey>(), long.MaxValue));
                while (renderer.HasPendingWorldUpload)
                    renderer.ProcessWorldUploads();
                return new RedFury.World.WorldLodNode(new RedFury.World.WorldEntityId(1, index), null, key, Matrix4x4.Identity, new Vector3(.5f, .5f, 0), 1, 100, 0, 0);
            }
            RedFury.World.WorldLodNode low = Place(1, 0), high = Place(2, 1);
            renderer.SetWorldSelection(new RedFury.World.WorldSelection([low], [low.Asset]));
            renderer.RenderTo(view);
            Assert.Equal(0, renderer.WorldFadingOut);
            // Replaced: it stays drawn, fading out, as long as the fade lasts, then goes.
            renderer.SetWorldSelection(new RedFury.World.WorldSelection([high], [high.Asset]));
            renderer.RenderTo(view);
            Assert.Equal(1, renderer.WorldFadingOut);
            Thread.Sleep(TimeSpan.FromSeconds(ViewportRenderer.LodFadeSeconds + .15));
            renderer.RenderTo(view);
            Assert.Equal(0, renderer.WorldFadingOut);
            var (type, message) = gpu.Device.PopErrorScopeSync(5_000_000_000);
            Assert.True(type == WebGpuSharp.ErrorType.NoError, message);
            renderer.Dispose();
            target.Destroy();
        }
    }

    [Fact]
    public void A_placement_joining_or_leaving_an_asset_moves_none_of_its_others_and_takes_the_gap_one_left()
    {
        GpuDevice gpu = TestGpu.Device();
        lock (gpu.Gate)
        {
            var renderer = new ViewportRenderer(gpu);
            WebGpuSharp.Texture target = gpu.Device.CreateTexture(new WebGpuSharp.TextureDescriptor
            {
                Usage = WebGpuSharp.TextureUsage.RenderAttachment,
                Dimension = WebGpuSharp.TextureDimension.D2,
                Size = new WebGpuSharp.Extent3D(64, 32, 1),
                Format = renderer.PresentFormat,
                MipLevelCount = 1,
                SampleCount = 1,
            });
            WebGpuSharp.TextureView view = target.CreateView();
            renderer.Resize(64, 32);
            renderer.WorldShown = true;
            renderer.Camera.Frame(new Vector3(0, -5, 1), 20);
            renderer.Camera.Free = true;
            renderer.Camera.Place(new Vector3(0, -5, 1), -MathF.PI / 2, 0);
            gpu.Device.PushErrorScope(WebGpuSharp.ErrorFilter.Validation);
            FiveFury.Drawables.Drawable drawable = FiveFury.Ydr.Ydr.Read(TestDrawables.Triangle()).Drawable;
            var key = new RedFury.World.WorldAssetKey(1, 2);
            Assert.True(renderer.AddWorldAsset(Rendering.World.WorldAsset.FromGta(key, DrawableScene.Build(drawable, []), [drawable]),
                new HashSet<RedFury.World.WorldAssetKey>(), long.MaxValue));
            while (renderer.HasPendingWorldUpload)
                renderer.ProcessWorldUploads();
            // Placements of the one asset, a metre apart.
            RedFury.World.WorldLodNode[] nodes = [.. Enumerable.Range(0, 80).Select(i => new RedFury.World.WorldLodNode(new RedFury.World.WorldEntityId(1, i), null, key,
                Matrix4x4.CreateTranslation(i, 0, 0), new Vector3(i + .5f, .5f, 0), 1, 100, 0, 0))];
            void Cover(params RedFury.World.WorldLodNode[] covered)
            {
                renderer.SetWorldSelection(new RedFury.World.WorldSelection(covered, [key]));
                renderer.RenderTo(view);
                Assert.True(renderer.WorldSourcesMatchCoverage());
            }
            void FadesEnd()
            {
                Thread.Sleep(TimeSpan.FromSeconds(ViewportRenderer.LodFadeSeconds + .15));
                renderer.RenderTo(view);
                Assert.Equal(0, renderer.WorldFadingOut);
                Assert.True(renderer.WorldSourcesMatchCoverage());
            }
            var (a, b, c, d, e) = (nodes[0], nodes[1], nodes[2], nodes[3], nodes[4]);
            Cover(a, b, c);
            Assert.Equal((3, 3), renderer.WorldSourceRows(key));
            // One leaves, fading out where it was; one comes in, fading in after them; the others stay put.
            Cover(a, c, d);
            Assert.Equal((4, 4), renderer.WorldSourceRows(key));
            Assert.Equal((0, 2, 3), (renderer.WorldSourceRow(a).Row, renderer.WorldSourceRow(c).Row, renderer.WorldSourceRow(d).Row));
            Assert.Equal(-1f, renderer.WorldSourceRow(b).Normal.M24);
            Assert.Equal(1f, renderer.WorldSourceRow(d).Normal.M24);
            Assert.Equal(0f, renderer.WorldSourceRow(a).Normal.M24);
            // Faded out, it leaves its row as a gap, which the next to come in takes.
            FadesEnd();
            Assert.Equal((4, 3), renderer.WorldSourceRows(key));
            Cover(a, c, d, e);
            Assert.Equal(1, renderer.WorldSourceRow(e).Row);
            Assert.Equal((4, 4), renderer.WorldSourceRows(key));
            // Once gaps are half the storage, it's laid out again without them.
            FadesEnd();
            Cover(nodes);
            Assert.Equal((80, 80), renderer.WorldSourceRows(key));
            FadesEnd();
            Cover(a);
            FadesEnd();
            Assert.Equal((80, 1), renderer.WorldSourceRows(key));
            Cover(a, b);
            Assert.Equal((2, 2), renderer.WorldSourceRows(key));
            var (type, message) = gpu.Device.PopErrorScopeSync(5_000_000_000);
            Assert.True(type == WebGpuSharp.ErrorType.NoError, message);
            renderer.Dispose();
            target.Destroy();
        }
    }

    private const long WorldShadowCount = Rendering.World.WorldShadowCascades.Count;
}
