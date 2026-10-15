using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

// Shared by the NPCs (merchant, stable keeper, courier): they stay exactly where you placed them, play an idle clip
// without an Animator Controller and keep their feet on the ground (an idle made for another body can push it down).
public class NpcIdle
{
    private readonly Transform root;
    private PlayableGraph graph;
    private Vector3 standPosition;
    private readonly Transform hips, leftFoot, rightFoot;
    private readonly float restFootHeight;

    // Create it in Awake: remembers how high the feet are in the pose you placed it with (before any animation runs)
    public NpcIdle(Transform root)
    {
        this.root = root;
        standPosition = root.position;
        Animator animator = root.GetComponentInChildren<Animator>();
        if (animator == null || !animator.isHuman) return;
        hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        if (hips != null && leftFoot != null && rightFoot != null)
            restFootHeight = Mathf.Min(leftFoot.position.y, rightFoot.position.y) - root.position.y;
    }

    // Call in Start: no physics pushing it around, and the idle clip (if any) starts playing
    public void Begin(AnimationClip idleClip, string owner)
    {
        standPosition = root.position;
        foreach (Rigidbody body in root.GetComponentsInChildren<Rigidbody>())
        {
            body.isKinematic = true;
            body.useGravity = false;
        }

        if (idleClip == null) return;
        Animator animator = root.GetComponentInChildren<Animator>();
        if (animator == null)
        {
            Debug.LogWarning($"{root.name}: {owner} needs an Animator (with the model's Avatar) to play the idle animation.");
            return;
        }
        animator.applyRootMotion = false;
        AnimationPlayableUtilities.PlayClip(animator, idleClip, out graph);
    }

    // Call in LateUpdate: keeps it in place (even if an animation tries to move it) and lifts the body onto its feet
    public void Hold()
    {
        if (root.position != standPosition) root.position = standPosition;
        if (graph.IsValid() && hips != null && leftFoot != null && rightFoot != null)
        {
            float wanted = root.position.y + restFootHeight, current = Mathf.Min(leftFoot.position.y, rightFoot.position.y);
            hips.position += Vector3.up * (wanted - current);
        }
    }

    public void Destroy()
    {
        if (graph.IsValid()) graph.Destroy();
    }

    // Turns the NPC to look at a point (only around the vertical axis)
    public static void Face(Transform t, Vector3 position)
    {
        Vector3 look = position - t.position;
        look.y = 0f;
        if (look.sqrMagnitude > 0.01f) t.rotation = Quaternion.LookRotation(look);
    }
}
