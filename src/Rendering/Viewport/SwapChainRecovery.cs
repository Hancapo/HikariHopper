using HikariHopper.Gpu;

namespace HikariHopper.Rendering;

/// <summary>What a viewport does with what its window's swap chain gave for a frame.</summary>
internal enum SwapChainStep
{
    /// <summary>A texture to draw into: the frame is drawn and shown.</summary>
    Draw,

    /// <summary>Nothing this frame, and nothing to change: the next frame asks again.</summary>
    Skip,

    /// <summary>The swap chain no longer fits the window: it's configured again, and the next frame asks again.</summary>
    Configure,

    /// <summary>It kept giving nothing: the swap chain is let go of and configured again, which makes a new one.</summary>
    Recreate,

    /// <summary>It gives nothing even made again, or an error (its device lost): the viewport stops, saying why.</summary>
    Fail,
}

/// <summary>
/// How a viewport gets its window's swap chain going again when it stops giving textures. A frame that gets none isn't
/// drawn or shown, and before this each such frame configured the swap chain again (all the frame's targets with it)
/// and drew nothing, for good when it never gave one again: the view showed its last frame while frames went on, no
/// error said why, and the world's targets were made anew every frame.
/// </summary>
internal sealed class SwapChainRecovery
{
    /// <summary>Frames in a row without a texture after which the window's surface is made again.</summary>
    internal const int RecreateAfter = 30;

    /// <summary>Frames in a row without a texture after which the viewport stops.</summary>
    internal const int FailAfter = 3 * RecreateAfter;

    /// <summary>The frames in a row the swap chain gave no texture.</summary>
    internal int Missed { get; private set; }

    internal SwapChainStep Next(SurfaceGetCurrentTextureStatus status, bool texture)
    {
        // Suboptimal still shows: the window's size, which is what mostly makes it so, is followed by the host.
        if (texture && status is SurfaceGetCurrentTextureStatus.SuccessOptimal or SurfaceGetCurrentTextureStatus.SuccessSuboptimal)
        {
            Missed = 0;
            return SwapChainStep.Draw;
        }
        // A minimized or hidden window shows nothing: nothing to set up again, and no count towards giving up.
        if (status == SurfaceGetCurrentTextureStatus.Occluded)
            return SwapChainStep.Skip;
        if (status == SurfaceGetCurrentTextureStatus.Error)
            return SwapChainStep.Fail;
        Missed++;
        if (Missed >= FailAfter)
            return SwapChainStep.Fail;
        if (Missed % RecreateAfter == 0)
            return SwapChainStep.Recreate;
        // A timeout is the GPU still showing earlier frames; anything else is a swap chain that has to be set up again.
        return status == SurfaceGetCurrentTextureStatus.Timeout ? SwapChainStep.Skip : SwapChainStep.Configure;
    }
}
