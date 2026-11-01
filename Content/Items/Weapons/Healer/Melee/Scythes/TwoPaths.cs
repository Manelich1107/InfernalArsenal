using System.Collections.Generic;
using CalamityMod;
using CalamityMod.Items;
using CalamityMod.Items.Materials;
using CalamityMod.Rarities;
using CalamityMod.Tiles.Furniture.CraftingStations;
using InfernalEclipseWeaponsDLC.Content.Projectiles.HealerPro.Scythes;
using InfernalEclipseWeaponsDLC.Core.Players;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using ThoriumMod.Items.BossThePrimordials.Dream;
using ThoriumMod.Items.HealerItems;

namespace InfernalEclipseWeaponsDLC.Content.Items.Weapons.Healer.Melee.Scythes
{
    public class TwoPaths : ScytheItem
    {
        // Left swings last about 60 ticks at base speed, like Calamity Hunt's Scythe of the Old God.
        private const float DarkTimeScale = 2f;

        private int swingDirection = 1;
        internal int spin;
        private int darkStage;

        // Animated sheet (20 frames x 3 ticks: gold and crimson bolts off each blade, breathing halo, blood drip, edge glint;
        // tools/make_held_animation.py).
        // TwoPaths.png stays the single still frame that the swing projectiles draw.
        public override string Texture => base.Texture + "Animated";

        public override void SetStaticDefaults()
        {
            SetStaticDefaultsToScythe();
            Main.RegisterItemAnimation(Type, new DrawAnimationVertical(3, 20));
            ItemID.Sets.AnimatesAsSoul[Type] = true;
            ItemID.Sets.ItemsThatAllowRepeatedRightClick[Type] = true;
            ItemID.Sets.SkipsInitialUseSound[Type] = true;
        }

        public override void SetDefaults()
        {
            SetDefaultsToScythe();
            Item.damage = 1550;
            scytheSoulCharge = 4;
            Item.width = 208;
            Item.height = 190;
            Item.value = CalamityGlobalItem.RarityHotPinkBuyPrice;
            Item.rare = ModContent.RarityType<HotPink>();
            Item.shoot = ModContent.ProjectileType<TwoPathsDarkHeld>();
            Item.useTime = Item.useAnimation = 30;
            Item.useStyle = ItemUseStyleID.Shoot;
            Item.holdStyle = 6;
            Item.noUseGraphic = true;
            Item.autoReuse = true;
            Item.Calamity().devItem = true;
        }

        public override void AddRecipes()
        {
            InfernalEclipseWeaponsDLC IEWeaponDLC = InfernalEclipseWeaponsDLC.Instance;

            Recipe recipe = Recipe.Create(Item.type, 1);

            if (IEWeaponDLC.ragnarok != null && IEWeaponDLC.calamitybardhealer != null)
            {
                recipe.AddIngredient(IEWeaponDLC.ragnarok.Find<ModItem>("AuricGreatscythe").Type, 1);
                recipe.AddIngredient(IEWeaponDLC.calamitybardhealer.Find<ModItem>("Disaster").Type, 1);
            }
            else if (IEWeaponDLC.ragnarok != null)
            {
                recipe.AddIngredient(IEWeaponDLC.ragnarok.Find<ModItem>("AuricGreatscythe").Type, 1);
                recipe.AddIngredient(ModContent.ItemType<AshesofAnnihilation>(), 10);
            }
            else if (IEWeaponDLC.calamitybardhealer != null)
            {
                recipe.AddIngredient(ModContent.ItemType<RealitySlasher>(), 1);
                recipe.AddIngredient(IEWeaponDLC.calamitybardhealer.Find<ModItem>("Disaster").Type, 1);
            }
            else
            {
                recipe.AddIngredient(ModContent.ItemType<RealitySlasher>(), 1);
                recipe.AddIngredient(ModContent.ItemType<AshesofAnnihilation>(), 10);
            }
            recipe.AddIngredient(ModContent.ItemType<ShadowspecBar>(), 5);
            recipe.AddTile(ModContent.TileType<DraedonsForge>());
            recipe.Register();
        }

        public override void ModifyTooltips(List<TooltipLine> tooltips)
        {
            // Lore lines
            tooltips.Add(new TooltipLine(Mod, "TwoSideLore1", Language.GetTextValue("Mods.InfernalEclipseWeaponsDLC.ItemTooltip.TwoSideLore1")) { OverrideColor = Color.MediumPurple });
            tooltips.Add(new TooltipLine(Mod, "TwoSideLore2", Language.GetTextValue("Mods.InfernalEclipseWeaponsDLC.ItemTooltip.TwoSideLore2")) { OverrideColor = Color.MediumPurple });
        }

        public override bool AltFunctionUse(Player player) => true;

        public override bool CanUseItem(Player player)
        {
            if (player.ownedProjectileCounts[ModContent.ProjectileType<TwoPathsDarkHeld>()] > 0 || player.ownedProjectileCounts[ModContent.ProjectileType<TwoPathsLightHeld>()] > 0
                || player.ownedProjectileCounts[ModContent.ProjectileType<TwoPathsBalanceSpin>()] > 0)
                return false;
            return base.CanUseItem(player);
        }

        public override bool Shoot(Player player, EntitySource_ItemUse_WithAmmo source, Vector2 position, Vector2 velocity, int type, int damage, float knockback)
        {
            Vector2 aim = (Main.MouseWorld - player.MountedCenter).SafeNormalize(Vector2.UnitX * player.direction);
            float time = player.itemAnimationMax > 0 ? (player.itemAnimationMax > player.itemTimeMax ? player.itemTimeMax : player.itemAnimationMax) : Item.useAnimation;
            // In balance both buttons bring out the whole scythe and spin it.
            if (player.GetModPlayer<TwoPathsPlayer>().Balance)
            {
                Projectile.NewProjectile(source, player.MountedCenter, aim, ModContent.ProjectileType<TwoPathsBalanceSpin>(), damage, knockback, player.whoAmI, player.direction);
                return false;
            }
            if (player.altFunctionUse == 2)
            {
                int index = Projectile.NewProjectile(source, player.MountedCenter, aim, ModContent.ProjectileType<TwoPathsLightHeld>(), damage, knockback, player.whoAmI, time, time);
                Main.projectile[index].direction = swingDirection;
                Main.projectile[index].netUpdate = true;
                swingDirection = -swingDirection;
                if (++spin > 3)
                    spin = 1;
                return false;
            }

            float darkTime = time * DarkTimeScale;
            Projectile.NewProjectile(source, player.MountedCenter, aim, type, damage, knockback, player.whoAmI, darkTime, darkTime, darkStage);
            darkStage = (darkStage + 1) % 3;
            return false;
        }

        // The emissive parts of the animated sheet (lightning, halo glow, edge glint) at full brightness on the ground too.
        public override void PostDrawInWorld(SpriteBatch spriteBatch, Color lightColor, Color alphaColor, float rotation, float scale, int whoAmI)
        {
            Texture2D glow = ModContent.Request<Texture2D>(Texture + "_Glow").Value;
            Rectangle frame = Main.itemAnimations[Type].GetFrame(glow);
            Vector2 origin = frame.Size() / 2f;
            spriteBatch.Draw(glow, Item.position - Main.screenPosition + origin, frame, Color.White, rotation, origin, scale, SpriteEffects.None, 0f);
        }

        public override void HoldStyle(Player player, Rectangle heldItemFrame)
        {
            player.itemLocation += new Vector2(-15f, 12f) * player.Directions;
        }

        public override void UseStyle(Player player, Rectangle heldItemFrame)
        {
            player.itemLocation = Vector2.Zero;
        }
    }
}
