using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class Arrow : MonoBehaviour
{
    private Rigidbody rb;
    private float damage;
    private bool hasHit, shotByEnemy;
    private Transform shooter;

    public void Launch(Vector3 velocity, float arrowDamage, Transform shooter)
    {
        rb = GetComponent<Rigidbody>();
        damage = arrowDamage;
        this.shooter = shooter;
        shotByEnemy = shooter != null && shooter.GetComponentInParent<EnemyAI_01>() != null;

        if (shooter != null)
            foreach (Collider mine in GetComponentsInChildren<Collider>())
                foreach (Collider theirs in shooter.GetComponentsInChildren<Collider>())
                    Physics.IgnoreCollision(mine, theirs);

        rb.isKinematic = false;
        rb.useGravity = true;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.linearVelocity = velocity;
        transform.rotation = Quaternion.LookRotation(velocity);

        WorldCleanup.Add(gameObject, WorldCleanup.Kind.Arrow);
    }

    void FixedUpdate()
    {
        if (!hasHit && rb != null && rb.linearVelocity.sqrMagnitude > 0.1f)
            transform.rotation = Quaternion.LookRotation(rb.linearVelocity);
    }

    void OnCollisionEnter(Collision collision)
    {
        if (hasHit) return;
        hasHit = true;
        SoundManager.Play(Sfx.ArrowHit, collision.contactCount > 0 ? collision.GetContact(0).point : transform.position);

        if (shotByEnemy)
        {
            // Enemy arrows hurt the player (blocking and dodging still work), never other enemies
            PlayerStats playerStats = collision.collider.GetComponentInParent<PlayerStats>();
            if (playerStats != null) playerStats.TakeDamage(damage, HitSource());
            else
            {
                // Multiplayer: a player who is on another computer
                RemotePlayerAvatar remote = collision.collider.GetComponentInParent<RemotePlayerAvatar>();
                if (remote != null) remote.ApplyDamage(damage, HitSource());
            }
        }
        else
        {
            EnemyAI_01 enemy = collision.collider.GetComponentInParent<EnemyAI_01>();
            if (enemy != null) enemy.TakeDamage(damage, HitSource(), 0f);
            else
            {
                IAnimalTarget animal = collision.collider.GetComponentInParent<IAnimalTarget>();
                if (animal != null && !animal.IsDead) animal.Hit(damage, HitSource(), 0f);
            }
        }

        rb.isKinematic = true;
        foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;
        transform.SetParent(collision.transform, true);
    }

    Vector3 HitSource() => shooter != null ? shooter.position : transform.position - transform.forward;

    public static GameObject CreateDefault()
    {
        GameObject arrow = new GameObject("Arrow");
        arrow.AddComponent<Rigidbody>().mass = 0.1f;
        CapsuleCollider col = arrow.AddComponent<CapsuleCollider>();
        col.direction = 2;
        col.radius = 0.03f;
        col.height = 0.8f;

        GameObject shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Object.Destroy(shaft.GetComponent<Collider>());
        shaft.transform.SetParent(arrow.transform, false);
        shaft.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        shaft.transform.localScale = new Vector3(0.02f, 0.4f, 0.02f);
        shaft.GetComponent<Renderer>().sharedMaterial = ParticleMaterials.CreateSolid("Arrow", new Color(0.42f, 0.28f, 0.14f)); // no pink in builds

        arrow.AddComponent<Arrow>();
        return arrow;
    }
}
