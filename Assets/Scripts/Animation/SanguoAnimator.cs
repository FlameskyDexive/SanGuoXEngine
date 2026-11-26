using System;

using XEngine.Animation;
using XEngine.Runtime;

namespace SanGuoXEngine.Animation;

/// <summary>
/// Plays a hero's animation states by name, the way the original game drives its Unity Animator:
/// one controller state per clip, no transitions, everything decided in code; and an additive
/// second layer for hit reactions that plays over whatever the base layer is doing.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><see cref="Play"/> starts a state of the hero's controller on the base layer, snapping or
/// crossfading. The controller is flat on purpose (see <c>SanguoClipBuilder</c>): what follows
/// what is the caller's decision.</item>
/// <item><see cref="InsertHurt"/> plays a state on an additive layer, like the original
/// controller's <c>hurt</c> layer: the difference between that clip and its first frame is added
/// to the base pose, so a hit recoils on top of a running attack instead of replacing it.</item>
/// <item>Root motion: the hero's Animator applies the root-motion curves of the clip (as Unity
/// does); gameplay may take over through <c>Animator.OnAnimatorMove</c>.</item>
/// </list>
/// This component holds no state machine of its own: hit-stop, combo windows and cooldowns are
/// gameplay (the combat kernel), not animation.
/// </remarks>
public sealed class SanguoAnimator : MonoBehaviour
{
    /// <summary>Fade used when the caller gives none.</summary>
    public const float DefaultFade = 0.1f;

    /// <summary>The float parameter every state of the generated controllers multiplies its speed
    /// with (a state's "Multiplier" in Unity's terms); <see cref="Speed"/> writes it.</summary>
    public const string SpeedParameter = "SpeedMultiplier";

    private Animator? _animator;

    // The hit-reaction layer, created on first use.
    private int _hurtLayer = -1;
    private AnimationMixerPlayable? _hurtMixer;
    private AnimationClipPlayable? _hurt;

    /// <summary>The engine Animator this component drives.</summary>
    public Animator Animator => _animator ??= GetComponent<Animator>() ?? throw new InvalidOperationException(
        $"'{GameObject.Name}' has no Animator for its {nameof(SanguoAnimator)}.");

    /// <summary>The clip playing on the base layer, or null before the first play.</summary>
    public string? Current => _animator != null ? _animator.CurrentClip?.Name : null;

    /// <summary>How far the base-layer state is, 0 at its start and 1 at its end (loops keep counting).</summary>
    public float NormalizedTime => _animator != null ? _animator.GetCurrentAnimatorStateInfo(0).normalizedTime : 0f;

    /// <summary>True while the base layer is fading from one state to another.</summary>
    public bool InTransition => _animator != null && _animator.IsInTransition(0);

    /// <summary>True once a non-looping state has reached its end. The pose then stays on the last
    /// frame (dying, knocked down) until the next <see cref="Play"/>.</summary>
    public bool Finished
    {
        get
        {
            if (_animator == null || _animator.IsInTransition(0)) return false;
            AnimatorStateInfo info = _animator.GetCurrentAnimatorStateInfo(0);
            return info.length > 0f && !info.isLooping && info.normalizedTime >= 1f;
        }
    }

    /// <summary>Playback speed: of the state that is running and of every state started from now
    /// on. Slow motion and hit-stop turn it down; put it back to 1 afterwards.</summary>
    public float Speed
    {
        get => _animator != null ? _animator.Speed : 1f;
        set
        {
            Animator animator = Animator;
            animator.Speed = value;
            // A controller state's speed is its own times the parameter, read every frame.
            animator.SetFloat(SpeedParameter, value);
            if (_hurt != null) _hurt.Speed = value;
        }
    }

    /// <summary>The hit-reaction state on the additive layer, or null when none is playing.</summary>
    public string? Hurt => _hurt != null && _hurt.Weight > 0f ? _hurt.Clip?.Name : null;

    /// <summary>True once the hit reaction reached its end; the caller then fades it out with
    /// <see cref="ClearHurt"/>.</summary>
    public bool HurtFinished => _hurt == null || _hurt.Clip == null || _hurt.NormalizedTime >= 1f;

    public override void OnDisable()
    {
        // Disabling the Animator tears its graph down, the layer with it.
        _hurtLayer = -1;
        _hurtMixer = null;
        _hurt = null;
        _animator = null;
    }

    /// <summary>Starts a controller state on the base layer. Returns false when the controller has
    /// no state of that name.</summary>
    /// <param name="fade">Crossfade time in seconds; 0 snaps to the new state.</param>
    public bool Play(string state, float fade = 0f)
    {
        Animator animator = Animator;
        if (!animator.HasState(state)) return false;
        animator.CrossFade(state, fade, 0);
        return true;
    }

    /// <summary>Plays a controller state on the additive layer over the base pose. Replaces the hit
    /// reaction playing there. Returns false when the controller has no state of that name.</summary>
    /// <param name="fade">Fade-in time in seconds; 0 snaps.</param>
    public bool InsertHurt(string state, float fade = DefaultFade)
    {
        Animator animator = Animator;
        if (!animator.HasState(state)) return false;
        AnimationClip? clip = FindMotion(state);
        if (clip == null) return false;

        if (_hurtLayer < 0 || _hurtMixer == null)
        {
            _hurtMixer = animator.AddLayer(null, AnimatorLayerBlendingMode.Additive, 1f, clip);
            _hurtLayer = animator.LayerCount - 1;
        }
        else
        {
            // Unity subtracts the clip's own first frame; the layer was built around the previous
            // hit's, so it is replaced for this one.
            animator.LayerMixer?.SetAdditiveReferencePose(_hurtLayer,
                AnimationLayerMixerPlayable.BuildAdditiveReferencePose(clip, animator.Skeleton!));
            animator.SetLayerWeight(_hurtLayer, 1f);
        }

        AnimationClipPlayable hurt = animator.PlayClipOnMixer(clip, fade, _hurtMixer);
        // The Animator's own wrap mode is Loop; a hit reaction plays once and holds its end.
        hurt.Wrap = clip.Wrap;
        _hurt = hurt;
        return true;
    }

    /// <summary>Fades the hit reaction out.</summary>
    /// <param name="fade">Fade-out time in seconds; 0 removes it at once.</param>
    public void ClearHurt(float fade = DefaultFade)
    {
        if (_hurt == null || _animator == null) return;
        if (_hurt.Clip != null) _animator.Stop(_hurt.Clip, fade);
        _hurt = null;
    }

    private AnimationClip? FindMotion(string state)
    {
        Animator animator = Animator;
        AnimatorController? controller = animator.Controller.Res;
        if (controller == null) return null;
        foreach (AnimatorController.State entry in controller.States)
        {
            if (entry.Name == state) return entry.Motion.Res;
        }
        return null;
    }
}
