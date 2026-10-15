using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

// Another player as you see them: a copy of the player model (no scripts) that follows what that player sends.
// Created by MultiplayerSync - nothing to set up.
public class RemotePlayerAvatar : MonoBehaviour
{
    public static readonly List<RemotePlayerAvatar> All = new List<RemotePlayerAvatar>();

    // Bones copied while riding (the rider pose is made by code, not by the Animator)
    public static readonly HumanBodyBones[] PoseBones =
    {
        HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest, HumanBodyBones.Neck, HumanBodyBones.Head,
        HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
        HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot,
        HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
        HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
    };

    public ulong OwnerId { get; private set; }
    public string PlayerName => gear != null && !string.IsNullOrEmpty(gear.playerName) ? gear.playerName : $"Player {OwnerId}";
    public bool IsDead { get; private set; }
    public bool HasState { get; private set; }
    public Vector3 TargetPosition => targetPosition;
    public AnimSync Sync { get; private set; }
    public AnimSync HorseSync { get; private set; }

    private Transform model;
    private Animator anim;
    private CapsuleCollider body;
    private Vector3 targetPosition;
    private float targetYaw;

    // Riding pose
    private Transform[] bones;
    private readonly Quaternion[] boneTargets = new Quaternion[PoseBones.Length];
    private Vector3 hipsTarget;
    private bool posing;

    // Gear
    private GearState gear;
    private readonly List<GameObject> gearObjects = new List<GameObject>();
    private string pathRightHand, pathSheath, pathHead, pathBowHand, pathBowSheath, pathBack, pathLantern, pathBody;

    // Their horse
    private GameObject horse;
    private string horseKey;
    private bool horseMissing;
    private Vector3 horsePosition;
    private float horseYaw;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => All.Clear();

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() => All.Remove(this);

    // ---------- Creation ----------

    // A copy of YOUR player's model (both players use the same character). Null while you have no player yet
    public static RemotePlayerAvatar Create(ulong ownerId)
    {
        GameObject local = PlayerRef.Object;
        if (local == null) return null;

        GameObject root = new GameObject($"Remote Player {ownerId}"); // in the open scene: gone when the scene changes
        root.transform.SetPositionAndRotation(local.transform.position, local.transform.rotation);

        RemotePlayerAvatar avatar = root.AddComponent<RemotePlayerAvatar>();
        avatar.OwnerId = ownerId;
        avatar.Build(local);
        return avatar;
    }

    void Build(GameObject local)
    {
        // A body enemies can hit (same size as the player's CharacterController). Ignore Raycast: cameras and foot IK don't see it
        gameObject.layer = 2;
        body = gameObject.AddComponent<CapsuleCollider>();
        CharacterController cc = local.GetComponent<CharacterController>();
        if (cc != null)
        {
            body.center = cc.center;
            body.radius = cc.radius;
            body.height = cc.height;
        }
        else
        {
            body.center = new Vector3(0f, 0.9f, 0f);
            body.radius = 0.35f;
            body.height = 1.8f;
        }

        // Where the gear goes, found on the local player
        PlayerEquipmentManager pem = PlayerEquipmentManager.Instance;
        Transform localRoot = local.transform;
        Animator localAnim = local.GetComponentInChildren<Animator>();
        if (pem != null)
        {
            pathRightHand = NetVisuals.PathOf(pem.rightHandWeaponHolder, localRoot);
            pathSheath = NetVisuals.PathOf(pem.sheathHolder, localRoot);
            pathHead = NetVisuals.PathOf(pem.headHolder, localRoot);
            pathBowHand = NetVisuals.PathOf(pem.leftHandBowHolder, localRoot);
            pathBowSheath = NetVisuals.PathOf(pem.bowSheathHolder, localRoot);
            pathBack = NetVisuals.PathOf(pem.backHolder, localRoot);
            Transform lantern = pem.lanternHolder != null ? pem.lanternHolder
                : localAnim != null && localAnim.isHuman ? localAnim.GetBoneTransform(HumanBodyBones.Hips) : null;
            pathLantern = NetVisuals.PathOf(lantern, localRoot);
            pathBody = pem.nakedBodyMesh != null ? NetVisuals.PathOf(pem.nakedBodyMesh.transform, localRoot) : null;
        }

        // The copy: no scripts, no physics, none of the local player's gear
        GameObject copy = NetVisuals.CopyHidden(local);
        foreach (string path in new[] { pathRightHand, pathSheath, pathHead, pathBowHand, pathBowSheath, pathBack, pathLantern })
            RemoveGearCopies(NetVisuals.Find(copy.transform, path));
        Transform bodyCopy = NetVisuals.Find(copy.transform, pathBody);
        if (bodyCopy != null)
        {
            RemoveGearCopies(bodyCopy.parent); // skinned armor sits next to the body
            bodyCopy.gameObject.SetActive(true);
        }
        NetVisuals.StripToVisual(copy);
        foreach (Transform t in copy.GetComponentsInChildren<Transform>(true))
            if (t.CompareTag("Player")) t.gameObject.tag = "Untagged";

        copy.name = "Model";
        copy.transform.SetParent(transform, false);
        copy.transform.localPosition = Vector3.zero;
        copy.transform.localRotation = Quaternion.identity;
        model = copy.transform;

        anim = copy.GetComponentInChildren<Animator>();
        if (anim != null)
        {
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            if (anim.isHuman)
            {
                bones = new Transform[PoseBones.Length];
                for (int i = 0; i < PoseBones.Length; i++) bones[i] = anim.GetBoneTransform(PoseBones[i]);
            }
        }
        Sync = new AnimSync(anim);

        targetPosition = transform.position;
        targetYaw = transform.eulerAngles.y;
    }

    static void RemoveGearCopies(Transform holder)
    {
        if (holder == null) return;
        var remove = new List<GameObject>();
        foreach (Transform child in holder)
            if (child.name.Contains("(Clone)")) remove.Add(child.gameObject);
        foreach (GameObject go in remove) DestroyImmediate(go);
    }

    // ---------- State from the network ----------

    public void SetState(Vector3 position, float yaw, bool dead, bool mounted)
    {
        if (!HasState || (position - transform.position).sqrMagnitude > 36f)
        {
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f)); // first time or teleported: jump there
        }
        HasState = true;
        targetPosition = position;
        targetYaw = yaw;
        IsDead = dead;
        if (body != null) body.enabled = !dead;
        if (!mounted) posing = false;
    }

    public void ReadPose(ref FastBufferReader r)
    {
        r.ReadValueSafe(out byte count);
        for (int i = 0; i < count; i++)
        {
            r.ReadValueSafe(out Quaternion q);
            if (i < boneTargets.Length) boneTargets[i] = q;
        }
        r.ReadValueSafe(out Vector3 hips);
        hipsTarget = hips;
        posing = bones != null;
    }

    public static void WritePose(ref FastBufferWriter w, Animator localAnim)
    {
        bool human = localAnim != null && localAnim.isHuman;
        w.WriteValueSafe((byte)PoseBones.Length);
        Transform hips = null;
        for (int i = 0; i < PoseBones.Length; i++)
        {
            Transform b = human ? localAnim.GetBoneTransform(PoseBones[i]) : null;
            if (i == 0) hips = b;
            w.WriteValueSafe(b != null ? b.localRotation : Quaternion.identity);
        }
        w.WriteValueSafe(hips != null ? hips.localPosition : Vector3.zero);
    }

    public void SetGear(GearState state)
    {
        if (state == null || (gear != null && gear.SameAs(state))) return;
        gear = state;
        RebuildGear();
    }

    // ---------- Their horse ----------

    public void SetHorse(string key, Vector3 position, float yaw, float animatorSpeed)
    {
        if (horse == null || horseKey != key)
        {
            if (horseMissing && horseKey == key) return;
            BuildHorse(key, position, yaw);
        }
        horsePosition = position;
        horseYaw = yaw;
        if (HorseSync != null && HorseSync.Animator != null) HorseSync.Animator.speed = animatorSpeed;
    }

    public void ClearHorse()
    {
        if (horse != null) Destroy(horse);
        horse = null;
        HorseSync = null;
        horseKey = null;
        horseMissing = false;
    }

    void BuildHorse(string key, Vector3 position, float yaw)
    {
        ClearHorse();
        horseKey = key;
        GameObject template = HorseTemplate(key);
        if (template == null)
        {
            horseMissing = true;
            return;
        }

        GameObject copy = NetVisuals.CopyHidden(template);
        NetVisuals.StripToVisual(copy);
        foreach (Renderer r in copy.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
        copy.name = $"Remote Horse {OwnerId}";
        copy.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        copy.transform.SetParent(null, true);
        SceneManager.MoveGameObjectToScene(copy, SceneManager.GetActiveScene());
        horse = copy;

        Animator horseAnim = copy.GetComponentInChildren<Animator>();
        if (horseAnim != null)
        {
            horseAnim.applyRootMotion = false;
            horseAnim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }
        HorseSync = new AnimSync(horseAnim);
    }

    // The horse model: the one bought at the stable, else the HorseSpawner's prefab, else any horse in the scene
    static GameObject HorseTemplate(string key)
    {
        HorseOffer offer = HorseOwnership.OfferOf(key);
        if (offer != null && offer.prefab != null) return offer.prefab;
        if (HorseSpawner.Instance != null && HorseSpawner.Instance.horsePrefab != null) return HorseSpawner.Instance.horsePrefab;
        HorseMount any = FindFirstObjectByType<HorseMount>();
        return any != null ? any.gameObject : null;
    }

    // ---------- Every frame ----------

    void Update()
    {
        float dt = Time.deltaTime;
        float k = 1f - Mathf.Exp(-14f * dt);
        transform.position = Vector3.Lerp(transform.position, targetPosition, k);
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0f, targetYaw, 0f), k);

        if (horse != null)
        {
            if ((horse.transform.position - horsePosition).sqrMagnitude > 36f) horse.transform.position = horsePosition;
            horse.transform.position = Vector3.Lerp(horse.transform.position, horsePosition, k);
            horse.transform.rotation = Quaternion.Slerp(horse.transform.rotation, Quaternion.Euler(0f, horseYaw, 0f), k);
        }
    }

    // After the Animator: the riding pose
    void LateUpdate()
    {
        if (!posing || bones == null) return;
        float k = 1f - Mathf.Exp(-20f * Time.deltaTime);
        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i] == null) continue;
            bones[i].localRotation = Quaternion.Slerp(bones[i].localRotation, boneTargets[i], k);
        }
        if (bones[0] != null) bones[0].localPosition = Vector3.Lerp(bones[0].localPosition, hipsTarget, k);
    }

    void OnDestroy()
    {
        if (horse != null) Destroy(horse);
    }

    // Name above the head
    void OnGUI()
    {
        Camera cam = Camera.main;
        if (cam == null || model == null) return;
        Vector3 head = transform.position + Vector3.up * (body != null ? body.height + 0.35f : 2.1f);
        Vector3 screen = cam.WorldToScreenPoint(head);
        if (screen.z <= 0f || screen.z > 60f) return;

        string label = IsDead ? PlayerName + " (dead)" : PlayerName;
        GUIStyle style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(Screen.height / 60f) };
        Rect rect = new Rect(screen.x - 150f, Screen.height - screen.y - 15f, 300f, 30f);
        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), label, style);
        GUI.color = Color.white;
        GUI.Label(rect, label, style);
    }

    // An enemy (on the host) hit this player: tell that player's game
    public void ApplyDamage(float amount, Vector3 attackerPosition)
    {
        if (IsDead || MultiplayerSync.Instance == null) return;
        MultiplayerSync.Instance.SendHurt(OwnerId, amount, attackerPosition);
    }

    // ---------- Gear (same placement as PlayerEquipmentManager) ----------

    void RebuildGear()
    {
        foreach (GameObject go in gearObjects) if (go != null) Destroy(go);
        gearObjects.Clear();
        if (model == null || gear == null) return;

        Transform bodyMesh = NetVisuals.Find(model, pathBody);
        if (bodyMesh != null) bodyMesh.gameObject.SetActive(true);

        PlayerItemData weapon = ItemDatabase.Find(gear.weapon);
        if (weapon != null) Held(weapon, gear.weaponDrawn ? pathRightHand : pathSheath, !gear.weaponDrawn);

        PlayerItemData bow = ItemDatabase.Find(gear.bow);
        if (bow != null)
        {
            bool sheathed = !gear.bowDrawn && pathBowSheath != null;
            Held(bow, sheathed ? pathBowSheath : pathBowHand, sheathed);
        }

        PlayerItemData chest = ItemDatabase.Find(gear.chest);
        if (chest != null && Skinned(chest) != null && bodyMesh != null) bodyMesh.gameObject.SetActive(false);

        Attachment(ItemDatabase.Find(gear.helmet), pathHead);
        Attachment(ItemDatabase.Find(gear.backpack), pathBack);
        Attachment(ItemDatabase.Find(gear.lantern), pathLantern);
    }

    void Attachment(PlayerItemData item, string holderPath)
    {
        if (item == null || item.itemPrefab == null) return;
        if (item.itemPrefab.GetComponentInChildren<SkinnedMeshRenderer>() != null && Skinned(item) != null) return;
        Held(item, holderPath, false);
    }

    void Held(PlayerItemData item, string holderPath, bool sheathed)
    {
        Transform holder = NetVisuals.Find(model, holderPath);
        if (item.itemPrefab == null || holder == null) return;

        GameObject obj = NetVisuals.CopyHidden(item.itemPrefab);
        NetVisuals.StripToVisual(obj);
        Transform t = obj.transform;
        t.SetParent(holder, false);
        t.SetLocalPositionAndRotation(sheathed ? item.sheathPositionOffset : item.equipPositionOffset,
                                      Quaternion.Euler(sheathed ? item.sheathRotationOffset : item.equipRotationOffset));
        Vector3 parentScale = holder.lossyScale;
        Vector3 prefabScale = item.itemPrefab.transform.localScale;
        t.localScale = new Vector3(prefabScale.x / parentScale.x, prefabScale.y / parentScale.y, prefabScale.z / parentScale.z);
        if (item.equipScale != Vector3.zero) t.localScale = item.equipScale;
        gearObjects.Add(obj);
    }

    // Armor made for the body: bound to this copy's bones by name
    GameObject Skinned(PlayerItemData item)
    {
        Transform bodyMesh = NetVisuals.Find(model, pathBody);
        if (item.itemPrefab == null || bodyMesh == null) return null;
        SkinnedMeshRenderer bodySkin = bodyMesh.GetComponent<SkinnedMeshRenderer>();

        GameObject obj = NetVisuals.CopyHidden(item.itemPrefab);
        NetVisuals.StripToVisual(obj);
        obj.transform.SetParent(bodyMesh.parent, false);
        obj.transform.SetLocalPositionAndRotation(bodyMesh.localPosition, bodyMesh.localRotation);
        obj.transform.localScale = bodyMesh.localScale;

        var boneMap = new Dictionary<string, Transform>();
        foreach (Transform bone in model.GetComponentsInChildren<Transform>(true))
            if (!bone.IsChildOf(obj.transform) && !boneMap.ContainsKey(bone.name)) boneMap.Add(bone.name, bone);

        foreach (SkinnedMeshRenderer mesh in obj.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            Transform[] oldBones = mesh.bones;
            Transform[] newBones = new Transform[oldBones.Length];
            for (int i = 0; i < oldBones.Length; i++)
            {
                newBones[i] = oldBones[i];
                for (Transform b = oldBones[i]; b != null; b = b.parent)
                    if (boneMap.TryGetValue(b.name, out Transform match)) { newBones[i] = match; break; }
            }
            mesh.bones = newBones;
            if (bodySkin != null) mesh.rootBone = bodySkin.rootBone;
        }
        gearObjects.Add(obj);
        return obj;
    }
}
