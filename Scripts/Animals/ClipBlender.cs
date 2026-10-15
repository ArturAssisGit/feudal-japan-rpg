using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

// Plays a few animation clips on an Animator WITHOUT an Animator Controller, blending from one to the next.
// Used by the animals (BirdAI, FoxAI). Looping clips repeat by themselves, the others stop on their last frame.
public class ClipBlender
{
    private PlayableGraph graph;
    private AnimationMixerPlayable mixer;
    private readonly AnimationClipPlayable[] playables;
    private readonly AnimationClip[] clips;
    private readonly bool[] loops;
    private readonly float[] weights, times;
    private int current;

    public bool Ready { get; private set; }
    public float Blend = 0.2f;

    // For a creature: warns about empty clip slots, finds (or adds) its Animator and starts playing the first clip
    public static ClipBlender Create(Component owner, string[] slotNames, AnimationClip[] clips, bool[] loops, float blendSeconds)
    {
        for (int i = 0; i < clips.Length; i++)
            if (clips[i] == null) Debug.LogWarning($"{owner.GetType().Name} on '{owner.name}': the {slotNames[i]} clip is empty. Drag the clips from the model's FBX into the slots.", owner);
        Animator animator = owner.GetComponentInChildren<Animator>();
        if (animator == null) animator = owner.gameObject.AddComponent<Animator>();
        animator.applyRootMotion = false;
        return new ClipBlender(owner.GetType().Name + " " + owner.name, animator, clips, loops) { Blend = blendSeconds };
    }

    public ClipBlender(string name, Animator animator, AnimationClip[] clips, bool[] loops)
    {
        this.clips = clips;
        this.loops = loops;
        int n = clips.Length;
        playables = new AnimationClipPlayable[n];
        weights = new float[n];
        times = new float[n];
        foreach (AnimationClip c in clips) if (c == null) return;

        graph = PlayableGraph.Create(name);
        graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        mixer = AnimationMixerPlayable.Create(graph, n);
        for (int i = 0; i < n; i++)
        {
            playables[i] = AnimationClipPlayable.Create(graph, clips[i]);
            playables[i].SetSpeed(0f); // the time is set by hand (see Update)
            graph.Connect(playables[i], 0, mixer, i);
            weights[i] = i == 0 ? 1f : 0f;
            mixer.SetInputWeight(i, weights[i]);
            times[i] = loops[i] ? Random.value * clips[i].length : 0f; // not all animals in sync
        }
        AnimationPlayableOutput output = AnimationPlayableOutput.Create(graph, name, animator);
        output.SetSourcePlayable(mixer);
        graph.Play();
        Ready = true;
    }

    public void Play(int index, bool restart = false)
    {
        if (!Ready) return;
        if (index != current || restart) { current = index; if (restart || !loops[index]) times[index] = 0f; }
    }

    public void Update(float dt, float[] speeds)
    {
        if (!Ready) return;
        int n = clips.Length;
        float step = Blend <= 0.01f ? 1f : dt / Blend;
        float sum = 0f;
        for (int i = 0; i < n; i++)
        {
            weights[i] = Mathf.MoveTowards(weights[i], i == current ? 1f : 0f, step);
            sum += weights[i];
        }
        if (sum < 0.001f) { weights[current] = 1f; sum = 1f; }

        for (int i = 0; i < n; i++)
        {
            mixer.SetInputWeight(i, weights[i] / sum);
            float len = Mathf.Max(0.01f, clips[i].length);
            float t = times[i] + dt * (speeds != null && i < speeds.Length ? speeds[i] : 1f);
            times[i] = loops[i] ? t % len : Mathf.Min(t, len);
            playables[i].SetTime(times[i]);
        }
    }

    public void Destroy()
    {
        if (graph.IsValid()) graph.Destroy();
        Ready = false;
    }
}
