using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameContent.Achievements;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.UI;
using Terraria.UI.Chat;

namespace InfiniTerraria.Content.GUI.Common;

public class CustomItemSlot : UIElement
{
    public enum ArmorType
    {
        Head,
        Chest,
        Leg
    }

    internal const int TickOffsetX = 6;
    internal const int TickOffsetY = 2;

    // Use reflection to access internal static Item[] singleSlotArray field from the ItemSlot class.
    private static readonly Item[] _singleSlotArray =
        typeof(ItemSlot).GetField("singleSlotArray", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null) as Item[];

    protected CroppedTexture2D backgroundTexture;
    protected bool forceToggleButton;

    protected Item item;
    protected float scale;
    protected ToggleVisibilityButton toggleButton;

    public CustomItemSlot(int context = ItemSlot.Context.InventoryItem, float scale = 1f,
        ArmorType defaultArmorIcon = ArmorType.Head)
    {
        Context = context;
        this.scale = scale;
        backgroundTexture = GetBackgroundTexture(context);
        EmptyTexture = GetEmptyTexture(context, defaultArmorIcon);
        ItemVisible = true;
        ForceToggleButton = false;

        item = new Item();
        item.SetDefaults();

        CalculateSize();
    }

    /// <summary>
    ///     The slot context from <see cref="ItemSlot.Context" />.
    /// </summary>
    public int Context { get; }

    /// <summary>
    ///     Whether the item in the slot is visible.
    /// </summary>
    public bool ItemVisible { get; set; }

    /// <summary>
    ///     Whether the slot is frozen and cannot be interacted with.
    /// </summary>
    public bool IsAccessFrozen { get; set; }

    /// <summary>
    ///     Whether items can only be taken from the slot, not placed into it.
    /// </summary>
    public bool TakeOnly { get; set; }

    /// <summary>
    ///     The text to display when the slot is hovered over.
    /// </summary>
    public string HoverText { get; set; }

    /// <summary>
    ///     A function to determine whether the mouse item can be placed in the slot.
    /// </summary>
    public Func<Item, bool> IsValidItem { get; set; }

    /// <summary>
    ///     The texture to display in the foreground of the slot when it is empty.
    /// </summary>
    public CroppedTexture2D EmptyTexture { get; set; }

    /// <summary>
    ///     The slot that the current item will move to when right-clicked.
    /// </summary>
    public CustomItemSlot Partner { get; set; }

    /// <summary>
    ///     The current item in the slot.
    /// </summary>
    public Item Item => item;

    /// <summary>
    ///     The scale of the slot.
    /// </summary>
    public float Scale
    {
        get => scale;
        set
        {
            scale = value;
            CalculateSize();
        }
    }

    /// <summary>
    ///     The background texture of the slot.
    /// </summary>
    public CroppedTexture2D BackgroundTexture
    {
        get => backgroundTexture;
        set
        {
            backgroundTexture = value;
            CalculateSize();
        }
    }

    /// <summary>
    ///     Whether to force a toggle button on the slot if the context does not allow one.
    /// </summary>
    public bool ForceToggleButton
    {
        get => forceToggleButton;
        set
        {
            forceToggleButton = value;
            var hasButton = forceToggleButton || HasToggleButton(Context);

            if (!hasButton)
            {
                if (toggleButton == null) return;

                RemoveChild(toggleButton);
                toggleButton = null;
            }
            else
            {
                toggleButton = new ToggleVisibilityButton();
                Append(toggleButton);
            }
        }
    }

    public bool ShouldDrawBackground { get; set; } = true;

    public event ItemChangedEventHandler ItemChanged;
    public event ItemVisiblityChangedEventHandler ItemVisibilityChanged;

    protected override void DrawSelf(SpriteBatch spriteBatch)
    {
        DoDraw(spriteBatch);

        if (ContainsPoint(Main.MouseScreen) && !PlayerInput.IgnoreMouseInterface)
        {
            Main.LocalPlayer.mouseInterface = true;

            if (toggleButton != null && toggleButton.ContainsPoint(Main.MouseScreen)) return;

            if (Main.mouseItem.IsAir || IsValidItem == null || IsValidItem(Main.mouseItem))
            {
                var tempContext = Context;
                var tempItem = Item.Clone();

                // fix if it's a vanity slot with no partner
                if (Main.mouseRightRelease && Main.mouseRight)
                {
                    if (Context == ItemSlot.Context.EquipArmorVanity)
                        tempContext = ItemSlot.Context.EquipArmor;
                    else if (Context == ItemSlot.Context.EquipAccessoryVanity)
                        tempContext = ItemSlot.Context.EquipAccessory;
                }

                if (Partner != null && Main.mouseRightRelease && Main.mouseRight)
                {
                    SwapWithPartner();
                    ItemChanged?.Invoke(this, new ItemChangedEventArgs(tempItem, Item));
                }
                else
                {
                    var reset = false;
                    if (IsAccessFrozen && Main.mouseLeftRelease && Main.mouseLeft)
                    {
                        Main.mouseLeftRelease = false;
                        Main.mouseLeft = false;
                        reset = true;
                    }

                    _singleSlotArray[0] = Item;
                    ItemSlot.OverrideHover(_singleSlotArray, tempContext);
                    if (Main.mouseItem.IsAir || !TakeOnly) ItemSlot.LeftClick(_singleSlotArray, tempContext);
                    ItemSlot.RightClick(_singleSlotArray, tempContext);
                    if (Main.mouseLeftRelease && Main.mouseLeft)
                        Recipe.FindRecipes();

                    ItemSlot.MouseHover(_singleSlotArray, tempContext);
                    item = _singleSlotArray[0];
                    Recipe.FindRecipes();

                    if (reset)
                    {
                        Main.mouseLeftRelease = true;
                        Main.mouseLeft = true;
                    }

                    if (tempItem.type != Item.type || tempItem.stack != Item.stack)
                        ItemChanged?.Invoke(this, new ItemChangedEventArgs(tempItem, Item));
                }

                if (!string.IsNullOrEmpty(HoverText)) Main.hoverItemName = HoverText;
            }
        }
    }

    /// <summary>
    ///     Set the item in the slot.
    /// </summary>
    /// <param name="newItem">item to put in the slot</param>
    /// <param name="fireItemChangedEvent">whether to fire the <see cref="ItemChanged" /> event</param>
    public virtual void SetItem(Item newItem, bool fireItemChangedEvent = true)
    {
        var tempItem = item.Clone();
        item = newItem.Clone();

        if (fireItemChangedEvent)
            ItemChanged?.Invoke(this, new ItemChangedEventArgs(tempItem, newItem));
    }

    private void DoDraw(SpriteBatch spriteBatch)
    {
        var rectangle = GetDimensions().ToRectangle();
        var itemTexture = EmptyTexture.Texture;
        var itemRectangle = EmptyTexture.Rectangle;
        var color = EmptyTexture.Color;
        var itemLightScale = 1f;

        if (Item.stack > 0)
        {
            itemTexture = TextureAssets.Item[Item.type].Value;
            itemRectangle = Main.itemAnimations[Item.type] != null
                ? Main.itemAnimations[Item.type].GetFrame(itemTexture)
                : itemTexture.Frame();
            color = Color.White;

            ItemSlot.GetItemLight(ref color, ref itemLightScale, Item);
        }
        else if (Item.type > ItemID.None)
        {
            Main.NewText("This should not happen. Please report this to the mod developer.");
        }

        if (ShouldDrawBackground && BackgroundTexture.Texture != null)
            spriteBatch.Draw(
                BackgroundTexture.Texture,
                rectangle.TopLeft(),
                BackgroundTexture.Rectangle,
                BackgroundTexture.Color,
                0f,
                Vector2.Zero,
                Scale,
                SpriteEffects.None,
                1f);

        if (itemTexture != null)
        {
            // copied from ItemSlot.Draw()
            var oversizedScale = 1f;
            if (itemRectangle.Width > 32 || itemRectangle.Height > 32)
            {
                if (itemRectangle.Width > itemRectangle.Height)
                    oversizedScale = 32f / itemRectangle.Width;
                else
                    oversizedScale = 32f / itemRectangle.Height;
            }

            oversizedScale *= Scale;

            if (ItemLoader.PreDrawInInventory(Item, spriteBatch, rectangle.Center(), itemRectangle,
                    item.GetAlpha(color), item.GetColor(color), new Vector2(
                        itemRectangle.Center.X - itemRectangle.Location.X,
                        itemRectangle.Center.Y - itemRectangle.Location.Y), oversizedScale * itemLightScale))
                spriteBatch.Draw(
                    itemTexture,
                    rectangle.Center(),
                    itemRectangle,
                    Item.color == Color.Transparent ? Item.GetAlpha(color) :
                    Item.type != ItemID.IronBar && Item.type != ItemID.Starfury ? Item.GetColor(color) :
                    Item.GetAlpha(color),
                    0f,
                    new Vector2(itemRectangle.Center.X - itemRectangle.Location.X,
                        itemRectangle.Center.Y - itemRectangle.Location.Y),
                    oversizedScale * itemLightScale,
                    SpriteEffects.None,
                    0f);
        }

        // position based on vanilla code
        if (Item.stack > 1)
            ChatManager.DrawColorCodedStringWithShadow(
                spriteBatch,
                FontAssets.ItemStack.Value,
                Item.stack.ToString(),
                GetDimensions().Position() + new Vector2(10f, 26f) * Scale,
                Color.White,
                0f,
                Vector2.Zero,
                new Vector2(Scale),
                -1f,
                Scale);
    }

    /// <summary>
    ///     Swap the current item with its partner slot.
    /// </summary>
    protected void SwapWithPartner()
    {
        // modified from vanilla code
        Utils.Swap(ref item, ref Partner.item);
        SoundEngine.PlaySound(SoundID.Grab);
        Recipe.FindRecipes();

        if (Item.stack <= 0) return;

        if (Context != 0)
        {
            if (Context - 8 <= 4 || Context - 16 <= 1)
                AchievementsHelper.HandleOnEquip(Main.LocalPlayer, Item, Context);
        }
        else
        {
            AchievementsHelper.NotifyItemPickup(Main.LocalPlayer, Item);
        }
    }

    /// <summary>
    ///     Calculate the size of the slot based on background texture and scale.
    /// </summary>
    internal void CalculateSize()
    {
        if (BackgroundTexture == CroppedTexture2D.Empty) return;

        var width = BackgroundTexture.Texture.Width * Scale;
        var height = BackgroundTexture.Texture.Height * Scale;

        Width.Set(width, 0f);
        Height.Set(height, 0f);
    }

    /// <summary>
    ///     Get the background texture of a slot based on its context.
    /// </summary>
    /// <param name="context">slot context</param>
    /// <returns>background texture of the slot</returns>
    public static CroppedTexture2D GetBackgroundTexture(int context)
    {
        Texture2D texture;
        var color = Main.inventoryBack;

        switch (context)
        {
            case ItemSlot.Context.EquipAccessory:
            case ItemSlot.Context.EquipArmor:
            case ItemSlot.Context.EquipGrapple:
            case ItemSlot.Context.EquipMount:
            case ItemSlot.Context.EquipMinecart:
            case ItemSlot.Context.EquipPet:
            case ItemSlot.Context.EquipLight:
                color = DefaultColors.EquipBack;
                texture = TextureAssets.InventoryBack3.Value;
                break;
            case ItemSlot.Context.EquipArmorVanity:
            case ItemSlot.Context.EquipAccessoryVanity:
                color = DefaultColors.EquipBack;
                texture = TextureAssets.InventoryBack8.Value;
                break;
            case ItemSlot.Context.EquipDye:
                color = DefaultColors.EquipBack;
                texture = TextureAssets.InventoryBack12.Value;
                break;
            case ItemSlot.Context.ChestItem:
                color = DefaultColors.InventoryItemBack;
                texture = TextureAssets.InventoryBack5.Value;
                break;
            case ItemSlot.Context.BankItem:
                color = DefaultColors.InventoryItemBack;
                texture = TextureAssets.InventoryBack2.Value;
                break;
            case ItemSlot.Context.GuideItem:
            case ItemSlot.Context.PrefixItem:
            case ItemSlot.Context.CraftingMaterial:
                color = DefaultColors.InventoryItemBack;
                texture = TextureAssets.InventoryBack4.Value;
                break;
            case ItemSlot.Context.TrashItem:
                color = DefaultColors.InventoryItemBack;
                texture = TextureAssets.InventoryBack7.Value;
                break;
            case ItemSlot.Context.ShopItem:
                color = DefaultColors.InventoryItemBack;
                texture = TextureAssets.InventoryBack6.Value;
                break;
            default:
                texture = TextureAssets.InventoryBack.Value;
                break;
        }

        return new CroppedTexture2D(texture, color);
    }

    /// <summary>
    ///     Get the empty texture of a slot based on its context.
    /// </summary>
    /// <param name="context">slot context</param>
    /// <param name="armorType">type of equipment in the slot</param>
    /// <returns>empty texture of the slot</returns>
    public static CroppedTexture2D GetEmptyTexture(int context, ArmorType armorType = ArmorType.Head)
    {
        var frame = -1;

        switch (context)
        {
            case ItemSlot.Context.EquipArmor:
                switch (armorType)
                {
                    case ArmorType.Head:
                        frame = 0;
                        break;
                    case ArmorType.Chest:
                        frame = 6;
                        break;
                    case ArmorType.Leg:
                        frame = 12;
                        break;
                }

                break;
            case ItemSlot.Context.EquipArmorVanity:
                switch (armorType)
                {
                    case ArmorType.Head:
                        frame = 3;
                        break;
                    case ArmorType.Chest:
                        frame = 9;
                        break;
                    case ArmorType.Leg:
                        frame = 15;
                        break;
                }

                break;
            case ItemSlot.Context.EquipAccessory:
                frame = 11;
                break;
            case ItemSlot.Context.EquipAccessoryVanity:
                frame = 2;
                break;
            case ItemSlot.Context.EquipDye:
                frame = 1;
                break;
            case ItemSlot.Context.EquipGrapple:
                frame = 4;
                break;
            case ItemSlot.Context.EquipMount:
                frame = 13;
                break;
            case ItemSlot.Context.EquipMinecart:
                frame = 7;
                break;
            case ItemSlot.Context.EquipPet:
                frame = 10;
                break;
            case ItemSlot.Context.EquipLight:
                frame = 17;
                break;
        }

        if (frame == -1) return CroppedTexture2D.Empty;

        var extraTextures = TextureAssets.Extra[54].Value;
        var rectangle = extraTextures.Frame(3, 6, frame % 3, frame / 3);
        rectangle.Width -= 2;
        rectangle.Height -= 2;

        return new CroppedTexture2D(extraTextures, DefaultColors.EmptyTexture, rectangle);
    }

    /// <summary>
    ///     Whether the slot has a visibility toggle button.
    /// </summary>
    public static bool HasToggleButton(int context)
    {
        return context == ItemSlot.Context.EquipAccessory ||
               context == ItemSlot.Context.EquipLight ||
               context == ItemSlot.Context.EquipPet;
    }

    public static class DefaultColors
    {
        public static readonly Color EmptyTexture = Color.White * 0.35f;
        public static readonly Color InventoryItemBack = Main.inventoryBack;
        public static readonly Color EquipBack = Color.White * 0.8f;
    }

    protected internal class ToggleVisibilityButton : UIElement
    {
        internal ToggleVisibilityButton()
        {
            Width.Set(TextureAssets.InventoryTickOn.Value.Width, 0f);
            Height.Set(TextureAssets.InventoryTickOn.Value.Height, 0f);
        }

        protected override void DrawSelf(SpriteBatch spriteBatch)
        {
            if (Parent is not CustomItemSlot slot) return;

            DoDraw(spriteBatch, slot);

            if (ContainsPoint(Main.MouseScreen) && !PlayerInput.IgnoreMouseInterface)
            {
                Main.LocalPlayer.mouseInterface = true;
                Main.hoverItemName =
                    Language.GetTextValue(slot.ItemVisible ? "LegacyInterface.59" : "LegacyInterface.60");

                if (Main.mouseLeftRelease && Main.mouseLeft)
                {
                    SoundEngine.PlaySound(SoundID.MenuTick);
                    slot.ItemVisible = !slot.ItemVisible;
                    slot.ItemVisibilityChanged?.Invoke(slot, new ItemVisibilityChangedEventArgs(slot.ItemVisible));
                }
            }
        }

        protected void DoDraw(SpriteBatch spriteBatch, CustomItemSlot slot)
        {
            var parentRectangle = Parent.GetDimensions().ToRectangle();
            var tickTexture =
                slot.ItemVisible ? TextureAssets.InventoryTickOn.Value : TextureAssets.InventoryTickOff.Value;

            Left.Set(parentRectangle.Width - Width.Pixels + TickOffsetX, 0f);
            Top.Set(-TickOffsetY, 0f);

            spriteBatch.Draw(
                tickTexture,
                GetDimensions().Position(),
                Color.White * 0.7f);
        }
    }
}

public struct CroppedTexture2D
{
    public Texture2D Texture { get; }
    public Rectangle Rectangle { get; set; }
    public Color Color { get; set; }

    public static readonly CroppedTexture2D Empty = new();

    public CroppedTexture2D(Texture2D texture) : this(texture, Color.White, texture.Bounds)
    {
    }

    public CroppedTexture2D(Texture2D texture, Color color) : this(texture, color, texture.Bounds)
    {
    }

    public CroppedTexture2D(Texture2D texture, Rectangle rectangle) : this(texture, Color.White, rectangle)
    {
    }

    public CroppedTexture2D(Texture2D texture, Color color, Rectangle rectangle)
    {
        Texture = texture;
        Color = color;
        Rectangle = rectangle;
    }

    public static bool operator ==(CroppedTexture2D ct1, CroppedTexture2D ct2)
    {
        return ct1.Texture == ct2.Texture &&
               ct1.Rectangle == ct2.Rectangle &&
               ct1.Color == ct2.Color;
    }

    public static bool operator !=(CroppedTexture2D ct1, CroppedTexture2D ct2)
    {
        return ct1.Texture != ct2.Texture ||
               ct1.Rectangle != ct2.Rectangle ||
               ct1.Color != ct2.Color;
    }

    public override bool Equals(object obj)
    {
        return obj is CroppedTexture2D ct &&
               EqualityComparer<Texture2D>.Default.Equals(Texture, ct.Texture) &&
               Rectangle.Equals(ct.Rectangle) &&
               Color.Equals(ct.Color);
    }

    public override int GetHashCode()
    {
        var hashCode = -893046046;
        hashCode = hashCode * -1521134295 + EqualityComparer<Texture2D>.Default.GetHashCode(Texture);
        hashCode = hashCode * -1521134295 + EqualityComparer<Rectangle>.Default.GetHashCode(Rectangle);
        return hashCode;
    }
}