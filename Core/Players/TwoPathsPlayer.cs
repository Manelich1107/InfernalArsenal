using CalamityMod;
using InfernalEclipseWeaponsDLC.Content.Items.Weapons.Healer.Melee.Scythes;
using InfernalEclipseWeaponsDLC.Content.Projectiles.HealerPro.Scythes;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;

namespace InfernalEclipseWeaponsDLC.Core.Players
{
    // 光暗两端。左键（暗攻击）是 Dark，右键（光攻击）是 Light。
    public enum TwoPathsSide
    {
        Dark,
        Light
    }

    // 【神冥歧径的能量与平衡状态】每个玩家一份。
    // - 两条能量：Dark（暗）和 Light（光），各 0~100。左键一类的攻击命中充暗，右键一类的命中充光。
    // - 充能按“一次出手”计：同一刀以及它放出的冤魂、审判斩、裂隙，不管打中几个敌人、打中几次，都只加一次。
    //   做法是每次出手先用 BeginAttack 领一个递增的 attackId，弹幕命中时把这个号交给 CreditHit；
    //   号不大于上次已经计过的号就忽略。
    // - 两条都满时进入平衡（Balance = true）。手里拿着这把武器时会立刻打断当前攻击，生成带“双镰合一”开场的平衡旋转。
    // - 平衡期间命中不再充能；旋转时由 TwoPathsBalanceSpin 调 DrainBalance 扣能量，两条都归零时平衡结束。
    // - 衰减：DecayDelay tick 没有命中后，两条能量每 tick 各掉 DecayPerTick。平衡期间也会衰减；切换武器后能量保留但照常衰减。
    // - 死亡时清空。
    // 能量条的显示在 Core/TwoPathsGauge.cs，它只读这里的数值。
    public class TwoPathsPlayer : ModPlayer
    {
        public const float MaxEnergy = 100f;
        // 每次出手命中加多少：暗 20（左键一刀慢，5 次命中充满），光 10（右键一刀快，10 次命中充满）。数值待第 4 阶段统一调整。
        public const float DarkGainPerHit = 20f;
        public const float LightGainPerHit = 10f;
        // 平衡旋转从满到空持续的秒数（只在旋转时消耗），由此算出每 tick 扣多少。
        public const float BalanceSeconds = 6f;
        public const float BalanceDrainPerTick = MaxEnergy / (BalanceSeconds * 60f);
        // 300 tick（5 秒）没有命中后开始衰减，每 tick 掉 0.4（满能量约 4 秒掉光）。
        public const int DecayDelay = 300;
        public const float DecayPerTick = 0.4f;

        public float Dark;
        public float Light;
        public bool Balance;
        // 上一次出手是哪一端。左右键切换时，新攻击据此先播放一个翻转镰刀的小动作（见各攻击弹幕的 flipTimer）。
        public TwoPathsSide LastSide = TwoPathsSide.Dark;

        // 下一个要发出的出手编号，以及两端各自最后计过能量的编号。
        private int nextAttackId = 1;
        private int lastCreditedDark;
        private int lastCreditedLight;
        // 距上次命中过了多少 tick，用来判断衰减。
        private int ticksSinceCredit;

        public bool HoldingTwoPaths => Player.HeldItem.type == ModContent.ItemType<TwoPaths>();

        // 左键或右键攻击开始时调用：记下这一端，返回这次出手的编号。
        public int BeginAttack(TwoPathsSide side)
        {
            LastSide = side;
            return nextAttackId++;
        }

        // 平衡旋转开始时调用。直接把两端“已计过的编号”推到这次的编号，这样旋转本身和它放出的所有弹幕都不会充能，
        // 哪怕平衡结束以后才命中也一样。
        public int BeginBalanceAttack()
        {
            int attackId = nextAttackId++;
            lastCreditedDark = lastCreditedLight = attackId;
            return attackId;
        }

        // 平衡旋转每 tick 调用，两条能量一起扣。满能量正好撑 BalanceSeconds 秒。
        public void DrainBalance(float amount)
        {
            Dark = MathHelper.Max(Dark - amount, 0f);
            Light = MathHelper.Max(Light - amount, 0f);
        }

        // 弹幕命中敌人时调用（经 TwoPathsHelper.Credit，只在弹幕主人的客户端执行）。
        public void CreditHit(TwoPathsSide side, int attackId)
        {
            if (attackId <= 0)
                return;

            // 任何命中都会重置衰减计时，平衡期间也是；但平衡期间不再充能。
            ticksSinceCredit = 0;
            if (Balance)
                return;
            if (side == TwoPathsSide.Dark)
            {
                // 同一次出手只计一次。
                if (attackId <= lastCreditedDark)
                    return;
                lastCreditedDark = attackId;
                Dark = MathHelper.Min(Dark + DarkGainPerHit, MaxEnergy);
            }
            else
            {
                if (attackId <= lastCreditedLight)
                    return;
                lastCreditedLight = attackId;
                Light = MathHelper.Min(Light + LightGainPerHit, MaxEnergy);
            }
        }

        public override void PostUpdate()
        {
            // 脱战衰减。
            if (++ticksSinceCredit > DecayDelay)
            {
                Dark = MathHelper.Max(Dark - DecayPerTick, 0f);
                Light = MathHelper.Max(Light - DecayPerTick, 0f);
            }

            // 两条都空时退出平衡；两条都满时进入平衡。
            if (Balance)
            {
                if (Dark <= 0f && Light <= 0f)
                    Balance = false;
            }
            else if (Dark >= MaxEnergy && Light >= MaxEnergy)
            {
                Balance = true;
                // 手里拿着这把武器：生成平衡旋转，进入特效等两半合上时由旋转弹幕播放。
                // 没拿着（比如切了武器）：在这里直接播放进入特效。
                if (Player.whoAmI == Main.myPlayer && !StartMerge())
                    BalanceEntryEffects();
            }

            // 平时握着镰刀时，前臂伸向握点（镰刀本身由 TwoPathsHoldLayer 绘制）。
            if (TwoPathsHoldLayer.ShowsIdleHold(Player))
                Player.SetCompositeArmFront(true, Player.CompositeArmStretchAmount.Full, TwoPathsHoldLayer.ArmRotation(Player));
        }

        // 手里拿着神冥歧径时进入平衡：打断正在进行的攻击，生成带“双镰合一”开场的平衡旋转（ai[1] = 1）。
        // 两半合上时旋转弹幕会调用 BalanceEntryEffects。返回 false 表示没拿着武器、没有生成。
        private bool StartMerge()
        {
            if (!HoldingTwoPaths)
                return false;

            int dark = ModContent.ProjectileType<TwoPathsDarkHeld>();
            int light = ModContent.ProjectileType<TwoPathsLightHeld>();
            int spin = ModContent.ProjectileType<TwoPathsBalanceSpin>();
            foreach (Projectile projectile in Main.ActiveProjectiles)
            {
                if (projectile.owner == Player.whoAmI && (projectile.type == dark || projectile.type == light || projectile.type == spin))
                    projectile.Kill();
            }

            Vector2 aim = (Main.MouseWorld - Player.MountedCenter).SafeNormalize(Vector2.UnitX * Player.direction);
            Item item = Player.HeldItem;
            // ai[0] 是旋转方向（光标在右边为 1，在左边为 -1），ai[1] = 1 表示播放合一开场。
            Projectile.NewProjectile(Player.GetSource_ItemUse(item), Player.MountedCenter, aim, spin, Player.GetWeaponDamage(item), Player.GetWeaponKnockback(item), Player.whoAmI, aim.X >= 0f ? 1f : -1f, 1f);
            return true;
        }

        // 进入平衡的特效（参照灾厄开启怒气和肾上腺素，但震屏更轻）：三层原版音效叠成的开启音效，
        // 角色身上一个 X 形尘埃（一条金色光尘、一条暗影焰尘），一圈金红混合的径向爆散，再轻推一次镜头。
        public void BalanceEntryEffects()
        {
            SoundEngine.PlaySound(SoundID.Item29 with { Pitch = -0.4f, Volume = 0.9f }, Player.Center);
            SoundEngine.PlaySound(SoundID.Item14 with { Pitch = -0.5f, Volume = 0.5f }, Player.Center);
            SoundEngine.PlaySound(SoundID.DD2_EtherianPortalOpen with { Pitch = 0.2f, Volume = 0.5f }, Player.Center);

            // X 形：两条对角线，各 48 颗尘埃，沿线从中心往两头散开。
            for (int line = 0; line < 2; line++)
            {
                Vector2 axis = (line == 0 ? new Vector2(1f, -1f) : new Vector2(1f, 1f)).SafeNormalize(Vector2.UnitX);
                int dustType = line == 0 ? DustID.GoldFlame : DustID.Shadowflame;
                for (int i = 0; i < 48; i++)
                {
                    float along = MathHelper.Lerp(-120f, 120f, i / 47f);
                    Dust dust = Dust.NewDustPerfect(Player.Center + axis * along, dustType, axis.RotatedBy(MathHelper.PiOver2) * Main.rand.NextFloat(-1.5f, 1.5f) + axis * along * 0.02f);
                    dust.noGravity = true;
                    dust.scale = Main.rand.NextFloat(1.2f, 1.8f);
                }
            }

            // 径向爆散。
            for (int i = 0; i < 60; i++)
            {
                Dust dust = Dust.NewDustPerfect(Player.Center, Main.rand.NextBool() ? DustID.GoldFlame : DustID.CrimsonTorch, Main.rand.NextVector2Unit() * Main.rand.NextFloat(4f, 10f));
                dust.noGravity = !Main.rand.NextBool(4);
                dust.scale = Main.rand.NextFloat(0.9f, 1.8f);
            }

            // 震屏用灾厄提供的 Player.SetScreenshake 扩展方法。
            Player.SetScreenshake(2.5f);
        }

        // 死亡时清空能量并退出平衡。
        public override void UpdateDead()
        {
            Dark = 0f;
            Light = 0f;
            Balance = false;
        }
    }
}
