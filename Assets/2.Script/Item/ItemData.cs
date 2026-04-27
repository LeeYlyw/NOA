using UnityEngine;

[CreateAssetMenu(fileName = "New Item", menuName = "Inventory/Item")]
public class ItemData : ScriptableObject
{
    public string itemName;
    public Sprite icon;
    public ItemType type;

    public enum ItemType
    {
        Teleport,
        Stealth,
        Heal,
        Resurrection
    }
}