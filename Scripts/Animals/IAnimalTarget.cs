using UnityEngine;

// Anything the player can hit that is not an EnemyAI_01 (foxes, deer...). PlayerCombat and Arrow call Hit().
public interface IAnimalTarget
{
    bool IsDead { get; }
    void Hit(float damage, Vector3 attackerPosition, float knockback);
}
