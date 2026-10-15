using UnityEngine;

// Auto-created. Pressing H when your horse is not in the scene creates it from its prefab.
// - With a HorseStable in the scene (horses must be bought): it creates the horse you chose at the stable.
// - Without a stable: it creates the "Horse Prefab" below (or any prefab named "Horse" inside a Resources folder).
public class HorseSpawner : MonoBehaviour
{
    public static HorseSpawner Instance { get; private set; }

    [Tooltip("Used only when there is no stable. The horse prefab (with HorseMount on it). Empty = looks for Resources/Horse")] public GameObject horsePrefab;
    public KeyCode callKey = KeyCode.H;
    [Tooltip("Seconds before you can call again (avoids two horses when you tap H)")] public float cooldown = 1f;

    private Transform player;
    private float nextAllowed;
    private bool warned;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<HorseSpawner>() != null) return;
        new GameObject("HorseSpawner").AddComponent<HorseSpawner>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // Loading a game: the horse you had in the world comes back where you left it
    public void ApplySavedHorse(HorseSave save)
    {
        if (!HorseOwnership.Required) return;

        HorseMount old = HorseMount.Main;
        if (old != null)
        {
            old.gameObject.SetActive(false);
            Destroy(old.gameObject);
        }

        if (save == null || !save.hasData || !save.horseInWorld) return;
        HorseOffer offer = HorseOwnership.OfferOf(save.horseKey);
        if (offer == null || offer.prefab == null || !HorseOwnership.IsOwned(save.horseKey)) return;

        GameObject horse = Instantiate(offer.prefab, save.horsePosition, Quaternion.Euler(0f, save.horseYaw, 0f));
        horse.name = offer.prefab.name;
        HorseMount mount = horse.GetComponentInChildren<HorseMount>();
        if (mount == null) { Destroy(horse); return; }
        mount.ownershipKey = save.horseKey;
        mount.ApplyStats(offer.SpeedMultiplier, offer.StaminaMultiplier, offer.AgilityMultiplier);
        mount.hiddenUntilCalled = false; // it is already standing there
    }

    void Update()
    {
        if (!Input.GetKeyDown(callKey) || Time.time < nextAllowed) return;
        if (HorseMount.PlayerMounted || !PlayerRef.Find(ref player)) return;

        PlayerEquipmentManager gear = PlayerEquipmentManager.Instance;
        if (gear != null && gear.IsOpen) return;

        HorseMount main = HorseMount.Main;
        GameObject prefab;
        HorseOffer offer = null;
        string key = "";

        if (HorseOwnership.Required)
        {
            // Horses must be bought: only the one you chose answers
            if (!HorseOwnership.HasActive)
            {
                nextAllowed = Time.time + cooldown;
                StableWindow.Toast("You don't have a horse. Buy one at the stable.");
                return;
            }
            key = HorseOwnership.Active;
            offer = HorseOwnership.ActiveOffer;
            if (main != null && main.ownershipKey == key) return; // your horse is already here: it comes by itself
            if (main != null && main.IsRidden) return;

            prefab = offer != null ? offer.prefab : null;
            if (prefab == null)
            {
                if (!warned) Debug.LogWarning($"HorseSpawner: there is no prefab for the horse '{key}' (is its HorseStable in this scene, with a Prefab set?).");
                warned = true;
                return;
            }
            if (main != null)
            {
                // Another horse (placed by hand or an old choice) is in the way
                main.gameObject.SetActive(false);
                Destroy(main.gameObject);
            }
        }
        else
        {
            if (main != null) return;
            if (horsePrefab == null) horsePrefab = Resources.Load<GameObject>("Horse");
            prefab = horsePrefab;
            if (prefab == null)
            {
                if (!warned) Debug.LogWarning("HorseSpawner: no horse in the scene and no prefab to create one. Drag the horse prefab to 'Horse Prefab' on the HorseSpawner object, or put it in Assets/Resources and name it 'Horse'.");
                warned = true;
                return;
            }
        }

        nextAllowed = Time.time + cooldown;
        GameObject horse = Instantiate(prefab, player.position, Quaternion.identity);
        horse.name = prefab.name;
        HorseMount mount = horse.GetComponentInChildren<HorseMount>();
        if (mount == null) { Debug.LogWarning("HorseSpawner: the prefab has no HorseMount component."); Destroy(horse); return; }

        mount.ownershipKey = key;
        if (offer != null) mount.ApplyStats(offer.SpeedMultiplier, offer.StaminaMultiplier, offer.AgilityMultiplier);
        mount.hiddenUntilCalled = true; // invisible until it is placed near you
        mount.RequestCall();
    }
}
