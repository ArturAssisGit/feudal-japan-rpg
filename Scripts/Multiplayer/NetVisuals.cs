using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Unity.Netcode;
using UnityEngine;

// Multiplayer helpers: copies of characters that only SHOW (no scripts, no physics), animator sync and the gear a player wears.
public static class NetVisuals
{
    private static Transform hiddenRoot;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => hiddenRoot = null;

    // A switched-off object: copies made under it don't run Awake/Start until they are moved out
    public static Transform HiddenRoot
    {
        get
        {
            if (hiddenRoot == null)
            {
                GameObject go = new GameObject("[Multiplayer hidden copies]");
                go.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(go);
                hiddenRoot = go.transform;
            }
            return hiddenRoot;
        }
    }

    // A copy of 'source' that has not started yet (its scripts never run if StripToVisual is called before it leaves HiddenRoot)
    public static GameObject CopyHidden(GameObject source) => UnityEngine.Object.Instantiate(source, HiddenRoot);

    // Leaves only what is needed to SEE the object: meshes, skinned meshes and the Animator.
    // Components that others depend on are removed after them (several passes), so Unity never refuses one.
    public static void StripToVisual(GameObject root)
    {
        for (int pass = 0; pass < 8; pass++)
        {
            bool removedAny = false;
            foreach (Component c in root.GetComponentsInChildren<Component>(true))
            {
                if (c == null || Keep(c) || NeededByAnother(c)) continue;
                UnityEngine.Object.DestroyImmediate(c);
                removedAny = true;
            }
            if (!removedAny) break;
        }
    }

    static bool Keep(Component c) =>
        c is Transform || c is Animator || c is MeshFilter || c is LODGroup || (c is Renderer && !(c is ParticleSystemRenderer));

    static bool NeededByAnother(Component c)
    {
        if (c is ParticleSystemRenderer && c.GetComponent<ParticleSystem>() != null) return true;
        if (c is Rigidbody && c.GetComponent<Joint>() != null) return true;
        foreach (Component other in c.GetComponents<Component>())
        {
            if (other == null || other == c || Keep(other)) continue;
            foreach (object attribute in other.GetType().GetCustomAttributes(typeof(RequireComponent), true))
            {
                RequireComponent rc = (RequireComponent)attribute;
                if (Is(rc.m_Type0, c) || Is(rc.m_Type1, c) || Is(rc.m_Type2, c)) return true;
            }
        }
        return false;
    }

    static bool Is(Type type, Component c) => type != null && type.IsInstanceOfType(c);

    public static string PathOf(Transform t, Transform root)
    {
        if (t == null || root == null || !t.IsChildOf(root)) return null;
        if (t == root) return "";
        string path = t.name;
        for (Transform p = t.parent; p != null && p != root; p = p.parent) path = p.name + "/" + path;
        return path;
    }

    public static Transform Find(Transform root, string path)
    {
        if (root == null || path == null) return null;
        return path.Length == 0 ? root : root.Find(path);
    }

    // "Bandit(Clone)", "Bandit (3)" and "Bandit (net)" are all "Bandit"
    public static string KeyOf(string objectName)
    {
        string n = objectName.Replace("(Clone)", "").Replace("(net)", "");
        n = Regex.Replace(n, @"\s*\(\d+\)\s*$", "");
        return n.Trim();
    }

    public static void Destroy(GameObject go)
    {
        if (go != null) UnityEngine.Object.Destroy(go);
    }
}

// Copies an Animator to the network (parameters + the state each layer is playing) and plays it back on a copy
public class AnimSync
{
    private const int MaxLayers = 8;
    private readonly Animator anim;
    private readonly AnimatorControllerParameter[] pars;

    public Animator Animator => anim;

    public AnimSync(Animator animator)
    {
        anim = animator;
        pars = animator != null && animator.runtimeAnimatorController != null ? animator.parameters : Array.Empty<AnimatorControllerParameter>();
    }

    bool Usable => anim != null && anim.isActiveAndEnabled && anim.runtimeAnimatorController != null;

    public void Write(ref FastBufferWriter w)
    {
        bool ok = Usable;
        byte count = ok ? (byte)Mathf.Min(pars.Length, 64) : (byte)0;
        w.WriteValueSafe(count);
        for (int i = 0; i < count; i++)
        {
            AnimatorControllerParameter p = pars[i];
            switch (p.type)
            {
                case AnimatorControllerParameterType.Float:
                    w.WriteValueSafe((byte)1);
                    w.WriteValueSafe(anim.GetFloat(p.nameHash));
                    break;
                case AnimatorControllerParameterType.Int:
                    w.WriteValueSafe((byte)2);
                    w.WriteValueSafe(anim.GetInteger(p.nameHash));
                    break;
                case AnimatorControllerParameterType.Bool:
                    w.WriteValueSafe(anim.GetBool(p.nameHash) ? (byte)4 : (byte)3);
                    break;
                default:
                    w.WriteValueSafe((byte)0); // triggers: the state below covers them
                    break;
            }
        }

        byte layers = ok ? (byte)Mathf.Min(anim.layerCount, MaxLayers) : (byte)0;
        w.WriteValueSafe(layers);
        for (int l = 0; l < layers; l++)
        {
            AnimatorStateInfo info = anim.IsInTransition(l) ? anim.GetNextAnimatorStateInfo(l) : anim.GetCurrentAnimatorStateInfo(l);
            w.WriteValueSafe(info.fullPathHash);
            w.WriteValueSafe(info.normalizedTime);
            w.WriteValueSafe(l == 0 ? 1f : anim.GetLayerWeight(l));
        }
    }

    // Reads what Write wrote. With no animator (or a null AnimSync via Skip) it only skips the data
    public void Read(ref FastBufferReader r) => ReadInto(this, ref r);

    public static void Skip(ref FastBufferReader r) => ReadInto(null, ref r);

    static void ReadInto(AnimSync sync, ref FastBufferReader r)
    {
        bool apply = sync != null && sync.Usable;
        Animator anim = apply ? sync.anim : null;
        AnimatorControllerParameter[] pars = sync != null ? sync.pars : Array.Empty<AnimatorControllerParameter>();

        r.ReadValueSafe(out byte count);
        for (int i = 0; i < count; i++)
        {
            r.ReadValueSafe(out byte type);
            bool match = apply && i < pars.Length;
            switch (type)
            {
                case 1:
                    r.ReadValueSafe(out float f);
                    if (match && pars[i].type == AnimatorControllerParameterType.Float) anim.SetFloat(pars[i].nameHash, f);
                    break;
                case 2:
                    r.ReadValueSafe(out int n);
                    if (match && pars[i].type == AnimatorControllerParameterType.Int) anim.SetInteger(pars[i].nameHash, n);
                    break;
                case 3:
                case 4:
                    if (match && pars[i].type == AnimatorControllerParameterType.Bool) anim.SetBool(pars[i].nameHash, type == 4);
                    break;
            }
        }

        r.ReadValueSafe(out byte layers);
        for (int l = 0; l < layers; l++)
        {
            r.ReadValueSafe(out int hash);
            r.ReadValueSafe(out float time);
            r.ReadValueSafe(out float weight);
            if (!apply || l >= anim.layerCount) continue;
            if (l > 0) anim.SetLayerWeight(l, weight);
            ForceState(anim, l, hash, time);
        }
    }

    // The copy plays a different state than the original (an attack, a dodge, a hit...): cross-fade to it
    static void ForceState(Animator anim, int layer, int hash, float time)
    {
        if (hash == 0) return;
        if (anim.GetCurrentAnimatorStateInfo(layer).fullPathHash == hash) return;
        if (anim.IsInTransition(layer) && anim.GetNextAnimatorStateInfo(layer).fullPathHash == hash) return;
        if (!anim.HasState(layer, hash)) return;
        anim.CrossFade(hash, 0.12f, layer, Mathf.Repeat(time, 1f));
    }
}

// What a player wears (item names from the ItemDatabase) - sent when it changes
[Serializable]
public class GearState
{
    public string playerName = "";
    public string weapon = "", bow = "", chest = "", helmet = "", backpack = "", lantern = "";
    public bool weaponDrawn, bowDrawn;

    public bool SameAs(GearState o) =>
        o != null && playerName == o.playerName && weapon == o.weapon && bow == o.bow && chest == o.chest && helmet == o.helmet &&
        backpack == o.backpack && lantern == o.lantern && weaponDrawn == o.weaponDrawn && bowDrawn == o.bowDrawn;

    public void Write(ref FastBufferWriter w)
    {
        w.WriteValueSafe(playerName ?? "");
        w.WriteValueSafe(weapon ?? "");
        w.WriteValueSafe(bow ?? "");
        w.WriteValueSafe(chest ?? "");
        w.WriteValueSafe(helmet ?? "");
        w.WriteValueSafe(backpack ?? "");
        w.WriteValueSafe(lantern ?? "");
        w.WriteValueSafe(weaponDrawn);
        w.WriteValueSafe(bowDrawn);
    }

    public static GearState Read(ref FastBufferReader r)
    {
        GearState g = new GearState();
        r.ReadValueSafe(out g.playerName);
        r.ReadValueSafe(out g.weapon);
        r.ReadValueSafe(out g.bow);
        r.ReadValueSafe(out g.chest);
        r.ReadValueSafe(out g.helmet);
        r.ReadValueSafe(out g.backpack);
        r.ReadValueSafe(out g.lantern);
        r.ReadValueSafe(out g.weaponDrawn);
        r.ReadValueSafe(out g.bowDrawn);
        return g;
    }

    // What the local player wears right now
    public static GearState CaptureLocal(string playerName)
    {
        GearState g = new GearState { playerName = playerName ?? "" };
        PlayerEquipmentManager pem = PlayerEquipmentManager.Instance;
        if (pem == null) return g;
        g.weapon = IdOf(pem.weaponSlotUI);
        g.bow = IdOf(pem.bowSlotUI);
        g.chest = IdOf(pem.chestSlotUI);
        g.helmet = IdOf(pem.helmetSlotUI);
        g.backpack = IdOf(pem.backpackSlotUI);
        g.lantern = IdOf(pem.lanternSlotUI);
        g.weaponDrawn = HasCopy(pem.rightHandWeaponHolder);
        Transform bow = pem.BowTransform;
        g.bowDrawn = bow != null && pem.leftHandBowHolder != null && bow.parent == pem.leftHandBowHolder;
        return g;
    }

    static string IdOf(PlayerItemSlot slot)
    {
        PlayerItemData item = slot != null ? slot.itemData : null;
        if (item == null) return "";
        if (item.IsUpgraded && item.baseItem != null) item = item.baseItem;
        return ItemDatabase.IdOf(item) ?? "";
    }

    static bool HasCopy(Transform holder)
    {
        if (holder == null) return false;
        foreach (Transform child in holder)
            if (child.name.Contains("(Clone)")) return true;
        return false;
    }
}
