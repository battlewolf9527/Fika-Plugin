﻿using Comfort.Common;
using EFT;
using EFT.Ballistics;
using Fika.Core.Coop.Players;
using UnityEngine;

namespace Fika.Core.Coop.ObservedClasses
{
    public class ObservedHostBridge(ObservedCoopPlayer observedPlayer) : BodyPartCollider.IPlayerBridge
    {
        private readonly ObservedCoopPlayer observedPlayer = observedPlayer;

        public IPlayer iPlayer
        {
            get
            {
                return observedPlayer;
            }
        }

        public float WorldTime
        {
            get
            {
                return Singleton<AbstractGame>.Instance.LastServerTimeStamp;
            }
        }

        public bool UsingSimplifiedSkeleton
        {
            get
            {
                return observedPlayer.UsedSimplifiedSkeleton;
            }
        }

        public void ApplyDamageInfo(DamageInfoStruct damageInfo, EBodyPart bodyPartType, EBodyPartColliderType bodyPartCollider, float absorbed)
        {
            if (damageInfo.DamageType.IsEnvironmental() || damageInfo.DamageType == EDamageType.Environment)
            {
                return;
            }

            if (damageInfo.DamageType is EDamageType.Landmine or EDamageType.Artillery)
            {
                observedPlayer.HandleExplosive(damageInfo, bodyPartType, bodyPartCollider);
                return;
            }

            if (damageInfo.Player != null && damageInfo.Player.iPlayer.IsYourPlayer || damageInfo.Player.iPlayer.IsAI)
            {
                observedPlayer.ApplyDamageInfo(damageInfo, bodyPartType, bodyPartCollider, absorbed);
            }
        }

        public ShotInfoClass ApplyShot(DamageInfoStruct damageInfo, EBodyPart bodyPart, EBodyPartColliderType bodyPartCollider, EArmorPlateCollider armorPlateCollider, ShotIdStruct shotId)
        {
            if (damageInfo.DamageType is EDamageType.Sniper)
            {
                return observedPlayer.HandleSniperShot(damageInfo, bodyPart, bodyPartCollider, armorPlateCollider, shotId);
            }

            if (damageInfo.Player != null && (damageInfo.Player.iPlayer.IsYourPlayer || damageInfo.Player.iPlayer.IsAI))
            {
                return observedPlayer.ApplyShot(damageInfo, bodyPart, bodyPartCollider, armorPlateCollider, shotId);
            }

            observedPlayer.ShotReactions(damageInfo, bodyPart);
            observedPlayer.ApplyHitDebuff(damageInfo.Damage, 0f, bodyPart, damageInfo.DamageType);
            return new()
            {
                PoV = EPointOfView.ThirdPerson,
                Penetrated = damageInfo.Penetrated,
                Material = MaterialType.Body
            };
        }

        public bool CheckArmorHitByDirection(BodyPartCollider bodypart, Vector3 hitpoint, Vector3 shotNormal, Vector3 shotDirection)
        {
            return observedPlayer.CheckArmorHitByDirection(bodypart);
        }

        public bool IsShotDeflectedByHeavyArmor(EBodyPartColliderType colliderType, EArmorPlateCollider armorPlateCollider, int shotSeed)
        {
            return observedPlayer.IsShotDeflectedByHeavyArmor(colliderType, armorPlateCollider, shotSeed);
        }

        public bool SetShotStatus(BodyPartCollider bodypart, EftBulletClass shot, Vector3 hitpoint, Vector3 shotNormal, Vector3 shotDirection)
        {
            return observedPlayer.SetShotStatus(bodypart, shot, hitpoint, shotNormal, shotDirection);
        }

        public bool TryGetArmorResistData(BodyPartCollider bodyPart, float penetrationPower, out ArmorResistanceStruct armorResistanceData)
        {
            return observedPlayer.TryGetArmorResistData(bodyPart, penetrationPower, out armorResistanceData);
        }

    }
}
