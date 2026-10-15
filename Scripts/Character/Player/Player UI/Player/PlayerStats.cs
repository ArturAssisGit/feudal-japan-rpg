using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PlayerStats : MonoBehaviour
{
    [Header("Health")]
    public float maxHealth = 100f;
    public float currentHealth;
    public Image healthBarFill;
    public CanvasGroup healthGroup;
    public float quitDelay = 2f;

    [Header("Health Regen")]
    public float healthRegenRate = 3f;
    [Tooltip("Seconds without taking damage before health starts regenerating")] public float healthRegenDelay = 4f;

    [Header("Stamina")]
    public float maxStamina = 100f;
    public float currentStamina;
    public Image staminaBarFill;
    public CanvasGroup staminaGroup;
    public float staminaRegenRate = 15f;

    [Header("Hunger")]
    public float maxHunger = 100f;
    public float currentHunger;
    [Tooltip("Hunger lost per minute (100 / 5 = empty after 20 minutes)")] public float hungerPerMinute = 5f;

    [Header("Thirst")]
    public float maxThirst = 100f;
    public float currentThirst;
    [Tooltip("Thirst lost per minute (100 / 8 = empty after ~12 minutes)")] public float thirstPerMinute = 8f;

    [Header("Hunger & Thirst Effects")]
    [Tooltip("Below this % health stops regenerating and stamina regenerates at half speed")] [Range(0f, 1f)] public float lowNeedsThreshold = 0.25f;
    [Tooltip("Health lost per second while starving or dehydrated (each one counts)")] public float emptyNeedsDamage = 1f;

    [Header("Hunger & Thirst Icons (only appear when low)")]
    [Tooltip("Icon shown when hungry (empty = shows the word 'Hungry')")] public Sprite hungerIcon;
    [Tooltip("Icon shown when thirsty (empty = shows the word 'Thirsty')")] public Sprite thirstIcon;
    [Tooltip("Position from the bottom-left corner of the screen")] public Vector2 needsIconPosition = new Vector2(30f, 30f);
    public float needsIconSize = 56f;
    [Tooltip("Extra height added on top of the position above (moves the Thirsty/Hungry icons up, above the health bar)")] public float needsIconExtraHeight = 70f;

    [Header("Defense")]
    [Tooltip("Defense needed to block half of the damage")] public float defenseScaling = 50f;

    [Header("Blocking")]
    [Range(0f, 1f), Tooltip("1 = blocks all damage, 0.5 = blocks half")] public float blockReduction = 1f;
    public float blockStaminaCost = 10f;
    [Tooltip("Only blocks attacks coming from within this angle in front of the player")] public float blockAngle = 120f;
    public Animator anim;

    [Header("Bar Visibility")]
    public float fadeSpeed = 6f;
    public float hideDelay = 1.5f;

    private float healthHideTimer, staminaHideTimer;
    private float lastDamageTime = -999f;
    private CanvasGroup hungerIconGroup, thirstIconGroup;
    private Graphic hungerIconGraphic, thirstIconGraphic;

    public bool IsDead => currentHealth <= 0f;
    public bool IsHungry => currentHunger <= maxHunger * lowNeedsThreshold;
    public bool IsThirsty => currentThirst <= maxThirst * lowNeedsThreshold;

    void Start()
    {
        if (anim == null) anim = GetComponentInChildren<Animator>();
        currentHealth = maxHealth;
        currentStamina = maxStamina;
        currentHunger = maxHunger;
        currentThirst = maxThirst;
        BuildNeedsIcons();

        SetGroupAlphaInstant(healthGroup, 0f);
        SetGroupAlphaInstant(staminaGroup, 0f);
    }

    void Update()
    {
        UpdateNeeds();
        bool lowNeeds = IsHungry || IsThirsty;

        float staminaRegen = staminaRegenRate * (lowNeeds ? 0.5f : 1f) * (ItemUpgrades.Active(OniSpirit.Nue) ? 1f + ItemUpgrades.NueStaminaBonus : 1f);
        currentStamina = Mathf.Min(currentStamina + staminaRegen * Time.deltaTime, maxStamina);

        if (!lowNeeds && currentHealth > 0f && Time.time >= lastDamageTime + healthRegenDelay)
            currentHealth = Mathf.Min(currentHealth + healthRegenRate * Time.deltaTime, maxHealth);

        if (healthBarFill != null) healthBarFill.fillAmount = currentHealth / maxHealth;
        if (staminaBarFill != null) staminaBarFill.fillAmount = currentStamina / maxStamina;

        UpdateBarVisibility(healthGroup, currentHealth, maxHealth, ref healthHideTimer);
        UpdateBarVisibility(staminaGroup, currentStamina, maxStamina, ref staminaHideTimer);
    }

    // ---------- Hunger & thirst ----------

    void UpdateNeeds()
    {
        if (currentHealth <= 0f) return;

        currentHunger = Mathf.Max(currentHunger - hungerPerMinute / 60f * Time.deltaTime, 0f);
        float thirstRate = thirstPerMinute * (ItemUpgrades.Active(OniSpirit.Kappa) ? ItemUpgrades.KappaThirstMultiplier : 1f);
        currentThirst = Mathf.Max(currentThirst - thirstRate / 60f * Time.deltaTime, 0f);

        // Starving or dehydrated: slowly lose health
        int emptyNeeds = (currentHunger <= 0f ? 1 : 0) + (currentThirst <= 0f ? 1 : 0);
        if (emptyNeeds > 0)
        {
            currentHealth = Mathf.Max(currentHealth - emptyNeedsDamage * emptyNeeds * Time.deltaTime, 0f);
            if (currentHealth <= 0f) Die();
        }

        UpdateNeedIcon(thirstIconGroup, thirstIconGraphic, IsThirsty, currentThirst <= 0f);
        UpdateNeedIcon(hungerIconGroup, hungerIconGraphic, IsHungry, currentHunger <= 0f);
    }

    // Fades the icon in when low; pulses red when completely empty
    void UpdateNeedIcon(CanvasGroup group, Graphic graphic, bool low, bool empty)
    {
        if (group == null) return;
        float pulse = (Mathf.Sin(Time.time * (empty ? 7f : 3f)) + 1f) * 0.5f;
        float targetAlpha = low ? Mathf.Lerp(0.6f, 1f, pulse) : 0f;
        group.alpha = Mathf.MoveTowards(group.alpha, targetAlpha, fadeSpeed * Time.deltaTime);

        if (graphic != null) graphic.color = empty ? Color.Lerp(Color.white, new Color(1f, 0.25f, 0.2f), pulse) : Color.white;
    }

    // Call these from food and drink items
    public void Eat(float amount) => currentHunger = Mathf.Min(currentHunger + amount, maxHunger);
    public void Drink(float amount) => currentThirst = Mathf.Min(currentThirst + amount, maxThirst);
    public void Heal(float amount) => currentHealth = Mathf.Min(currentHealth + amount, maxHealth);

    void BuildNeedsIcons()
    {
        GameObject canvasObject = new GameObject("NeedsIcons", typeof(Canvas), typeof(CanvasScaler));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        thirstIconGroup = CreateNeedIcon(canvasObject.transform, "Thirsty", thirstIcon, needsIconPosition + new Vector2(0f, needsIconExtraHeight), out thirstIconGraphic);
        hungerIconGroup = CreateNeedIcon(canvasObject.transform, "Hungry", hungerIcon, needsIconPosition + new Vector2(needsIconSize + 12f, needsIconExtraHeight), out hungerIconGraphic);
    }

    CanvasGroup CreateNeedIcon(Transform parent, string label, Sprite sprite, Vector2 position, out Graphic graphic)
    {
        GameObject root = new GameObject(label + "Icon", typeof(RectTransform), typeof(CanvasGroup));
        root.transform.SetParent(parent, false);
        RectTransform rect = root.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = Vector2.zero;
        rect.pivot = Vector2.zero;
        rect.anchoredPosition = position;
        rect.sizeDelta = sprite != null ? Vector2.one * needsIconSize : new Vector2(needsIconSize * 1.6f, needsIconSize * 0.5f);

        if (sprite != null)
        {
            Image image = root.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            graphic = image;
        }
        else
        {
            // No icon assigned yet: show the word instead
            TextMeshProUGUI text = root.AddComponent<TextMeshProUGUI>();
            text.text = label;
            text.fontSize = 22;
            text.alignment = TextAlignmentOptions.BottomLeft;
            text.raycastTarget = false;
            if (LootWindow.Instance != null && LootWindow.Instance.font != null) text.font = LootWindow.Instance.font;
            graphic = text;
        }

        CanvasGroup group = root.GetComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        return group;
    }

    void UpdateBarVisibility(CanvasGroup group, float current, float max, ref float hideTimer)
    {
        if (group == null) return;
        float targetAlpha = 1f;
        if (current < max) hideTimer = hideDelay;
        else if (hideTimer > 0f) hideTimer -= Time.deltaTime;
        else targetAlpha = 0f;

        group.alpha = Mathf.MoveTowards(group.alpha, targetAlpha, fadeSpeed * Time.deltaTime);
    }

    void SetGroupAlphaInstant(CanvasGroup group, float alpha) { if (group != null) group.alpha = alpha; }

    public bool TryConsumeStamina(float cost)
    {
        if (currentStamina < cost) return false;
        currentStamina -= cost;
        return true;
    }

    public void TakeDamage(float amount) => TakeDamage(amount, null);

    public void TakeDamage(float amount, Vector3? attackerPosition) => TakeDamage(amount, attackerPosition, null);

    // hitSound = impact sound of the weapon that hit (enemy / boss); null = none (e.g. arrows play their own)
    public void TakeDamage(float amount, Vector3? attackerPosition, Sfx? hitSound)
    {
        // Dead, dodged it, or in a finisher: no damage
        if (currentHealth <= 0f || PlayerDodge.IsInvulnerable || PlayerFinisher.IsActive) return;

        if (IsBlocking(attackerPosition))
        {
            float cost = blockStaminaCost * (PlayerEquipmentManager.PerkActive(BackpackPerk.FreeBlock) ? 0.5f : 1f);
            if (TryConsumeStamina(cost))
            {
                amount *= 1f - blockReduction;
                SoundManager.Play(Sfx.Block, transform.position + Vector3.up * 1.2f);
                Debug.Log("Blocked!");
                if (amount <= 0f) return;
            }
            else Debug.Log("Not enough stamina to block!");
        }

        lastDamageTime = Time.time;

        float defense = PlayerEquipmentManager.Instance != null ? PlayerEquipmentManager.Instance.TotalDefense : 0f;
        float finalDamage = amount * defenseScaling / (defenseScaling + defense);
        if (ItemUpgrades.Active(OniSpirit.Gashadokuro)) finalDamage *= ItemUpgrades.GashadokuroDamageTaken; // Oni Stone perk

        currentHealth = Mathf.Max(currentHealth - finalDamage, 0f);

        // Blood flies away from the attacker
        Collider body = GetComponent<Collider>();
        Vector3 hitPoint = body != null ? body.bounds.center + Vector3.up * body.bounds.extents.y * 0.3f : transform.position + Vector3.up * 1.2f;
        Vector3 away = attackerPosition.HasValue ? hitPoint - attackerPosition.Value : -transform.forward;
        away.y = 0f;
        BloodEffects.Spawn(hitPoint, away.normalized + Vector3.up * 0.35f);
        if (hitSound.HasValue) SoundManager.Play(hitSound.Value, hitPoint);
        SoundManager.Play(currentHealth <= 0f ? Sfx.PlayerDeath : Sfx.PlayerHurt, hitPoint);
        Debug.Log($"Player took {finalDamage:0.#} damage ({amount} - defense {defense})");
        if (currentHealth <= 0f) { Die(); return; }

        // "Combat Hit" animation (state name set in PlayerCombat -> Get Hit)
        foreach (PlayerCombat combat in GetComponentsInChildren<PlayerCombat>())
            if (combat.enabled) combat.PlayHitReaction();
    }

    bool IsBlocking(Vector3? attackerPosition)
    {
        if (anim == null || !anim.GetBool("isBlocking")) return false;
        if (attackerPosition == null) return true;

        Vector3 toAttacker = attackerPosition.Value - transform.position;
        toAttacker.y = 0f;
        return Vector3.Angle(transform.forward, toAttacker) <= blockAngle * 0.5f;
    }

    void Die()
    {
        Debug.Log("Player died!");
        EnemyAI_01.ResetBossesAfterPlayerDeath(); // the boss heals to 100%

        foreach (PlayerCombat combat in GetComponentsInChildren<PlayerCombat>())
        {
            combat.ResetCombatState();
            combat.enabled = false;
        }
        foreach (PlayerMovement movement in GetComponentsInChildren<PlayerMovement>()) movement.enabled = false;

        // Death animation (needs a "DieTrigger" parameter + Any State -> Death transition)
        if (HasAnimParameter("DieTrigger")) anim.SetTrigger("DieTrigger");

        // Death screen with "Wake up nearby" / "Respawn at tent"
        if (RespawnSystem.Instance != null) RespawnSystem.Instance.OnPlayerDied(this);
        else StartCoroutine(QuitAfterDelay());
    }

    // Brings the player back to life (called by RespawnSystem after moving the player)
    public void Revive(float healthPercent = 1f)
    {
        currentHealth = maxHealth * Mathf.Clamp01(healthPercent);
        currentStamina = maxStamina;
        currentHunger = Mathf.Max(currentHunger, maxHunger * 0.3f);
        currentThirst = Mathf.Max(currentThirst, maxThirst * 0.3f);
        lastDamageTime = -999f;

        // Stand up again: the Animator goes back to its starting state (idle)
        if (anim != null)
        {
            anim.Rebind();
            anim.Update(0f);
        }

        foreach (PlayerMovement movement in GetComponentsInChildren<PlayerMovement>()) movement.enabled = true;
        foreach (PlayerCombat combat in GetComponentsInChildren<PlayerCombat>())
        {
            combat.enabled = true;
            combat.ResetCombatState(); // sword back in the sheath, out of combat mode (after the Animator was reset)
        }
        Debug.Log("Player respawned");
    }

    bool HasAnimParameter(string parameterName)
    {
        if (anim == null) return false;
        foreach (AnimatorControllerParameter p in anim.parameters)
            if (p.name == parameterName) return true;
        return false;
    }

    System.Collections.IEnumerator QuitAfterDelay()
    {
        yield return new WaitForSeconds(quitDelay);

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
