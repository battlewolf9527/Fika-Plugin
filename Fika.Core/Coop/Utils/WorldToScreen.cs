﻿using EFT.Animations;
using EFT.CameraControl;
using EFT.InventoryLogic;
using Fika.Core.Coop.Players;
using UnityEngine;

namespace Fika.Core.Coop.Utils
{
    /// <summary>
    /// Class to convert screen space to world space
    /// </summary>
    public static class WorldToScreen
    {
#if DEBUG
        private static WorldToScreenDebugger debugger;

        public static bool ToggleDebugger()
        {
            if (debugger != null)
            {
                GameObject.Destroy(debugger);
                debugger = null;
                return true;
            }

            debugger = CameraClass.Instance.Camera.gameObject.AddComponent<WorldToScreenDebugger>();
            return false;
        }
#endif

        public static bool GetScreenPoint(Vector3 worldPosition, CoopPlayer mainPlayer, out Vector3 screenPoint, bool useOpticCamera = true, bool skip = false)
        {
            CameraClass worldCameraInstance = CameraClass.Instance;
            Camera worldCamera = worldCameraInstance.Camera;

            screenPoint = Vector3.zero;

            if (mainPlayer == null || worldCamera == null)
            {
                return false;
            }

            ProceduralWeaponAnimation weaponAnimation = mainPlayer.ProceduralWeaponAnimation;

            if (useOpticCamera && weaponAnimation != null)
            {
                if (weaponAnimation.IsAiming && weaponAnimation.CurrentScope.IsOptic)
                {
                    Camera opticCamera = worldCameraInstance.OpticCameraManager.Camera;
                    if (GetScopeZoomLevel(weaponAnimation) > 1f)
                    {
                        int width = worldCameraInstance.SSAA.GetInputWidth();
                        int height = worldCameraInstance.SSAA.GetInputHeight();
                        if (worldCameraInstance.SSAA.GetCurrentSSRatio() > 1)
                        {
                            width = Screen.width;
                            height = Screen.height;
                        }
                        Vector3 opticCenterScreenPosition = GetOpticCenterScreenPosition(weaponAnimation, worldCamera);
                        Vector3 opticCenterScreenOffset = opticCenterScreenPosition - (new Vector3(width, height, 0f) / 2);

                        float opticScale = height / opticCamera.scaledPixelHeight;
                        Vector3 opticCameraOffset = new(worldCamera.pixelWidth / 2 - opticCamera.pixelWidth / 2, worldCamera.pixelHeight / 2 - opticCamera.pixelHeight / 2, 0);
                        Vector3 opticScreenPoint = (opticCamera.WorldToScreenPoint(worldPosition) + opticCameraOffset) * opticScale;

#if DEBUG
                        if (debugger != null)
                        {
                            debugger.UpdateData(width, height, opticCenterScreenPosition, opticCenterScreenOffset,
                                opticCameraOffset, opticScreenPoint);
                        }
#endif

                        if (opticScreenPoint.z > 0f)
                        {
                            screenPoint = opticScreenPoint + opticCenterScreenOffset;
                        }
                    }
                }
            }

            // Not able to find a zoomed optic screen point
            if (screenPoint == Vector3.zero)
            {
                screenPoint = worldCamera.WorldToScreenPoint(worldPosition);
            }

            if (screenPoint.z > 0f)
            {
                return true;
            }

            return skip;
        }

        private static float GetScopeZoomLevel(ProceduralWeaponAnimation weaponAnimation)
        {
            SightComponent weaponSight = weaponAnimation.CurrentAimingMod;

            if (weaponSight == null)
            {
                return 1f;
            }

            if (weaponSight.ScopeZoomValue != 0)
            {
                return weaponSight.ScopeZoomValue;
            }

            return weaponSight.GetCurrentOpticZoom();
        }

        private static Vector3 GetOpticCenterScreenPosition(ProceduralWeaponAnimation weaponAnimation, Camera worldCamera)
        {
            if (weaponAnimation == null)
            {
                return Vector3.zero;
            }

            OpticSight currentOptic = CameraClass.Instance.OpticCameraManager.CurrentOpticSight;
            if (currentOptic == null)
            {
                return Vector3.zero;
            }

            Transform lensTransform = currentOptic.LensRenderer.transform;
            return worldCamera.WorldToScreenPoint(lensTransform.position);
        }

        public static void TargetOutOfSight(bool outOfSight, Vector3 indicatorPosition, RectTransform rectTransform, RectTransform canvasRect)
        {
            if (outOfSight)
            {
                rectTransform.rotation = Quaternion.Euler(RotationOutOfSightTargetindicator(indicatorPosition, canvasRect));
            }
            else
            {
                rectTransform.rotation = Quaternion.identity;
            }
        }

        public static Vector3 OutOfRangeindicatorPositionB(Vector3 indicatorPosition, RectTransform canvasRect, float outOfSightOffset)
        {
            indicatorPosition.z = 0f;

            Vector3 canvasCenter = new Vector3(canvasRect.rect.width / 2f, canvasRect.rect.height / 2f, 0f) * canvasRect.localScale.x;
            indicatorPosition -= canvasCenter;

            float divX = (canvasRect.rect.width / 2f - outOfSightOffset) / Mathf.Abs(indicatorPosition.x);
            float divY = (canvasRect.rect.height / 2f - outOfSightOffset) / Mathf.Abs(indicatorPosition.y);

            if (divX < divY)
            {
                float angle = Vector3.SignedAngle(Vector3.right, indicatorPosition, Vector3.forward);
                indicatorPosition.x = Mathf.Sign(indicatorPosition.x) * (canvasRect.rect.width * 0.5f - outOfSightOffset) * canvasRect.localScale.x;
                indicatorPosition.y = Mathf.Tan(Mathf.Deg2Rad * angle) * indicatorPosition.x;
            }
            else
            {
                float angle = Vector3.SignedAngle(Vector3.up, indicatorPosition, Vector3.forward);

                indicatorPosition.y = Mathf.Sign(indicatorPosition.y) * (canvasRect.rect.height / 2f - outOfSightOffset) * canvasRect.localScale.y;
                indicatorPosition.x = -Mathf.Tan(Mathf.Deg2Rad * angle) * indicatorPosition.y;
            }

            indicatorPosition += canvasCenter;
            return indicatorPosition;
        }

        public static Vector3 RotationOutOfSightTargetindicator(Vector3 indicatorPosition, RectTransform canvasRect)
        {
            Vector3 canvasCenter = new Vector3(canvasRect.rect.width / 2f, canvasRect.rect.height / 2f, 0f) * canvasRect.localScale.x;
            float angle = Vector3.SignedAngle(Vector3.up, indicatorPosition - canvasCenter, Vector3.forward);
            return new Vector3(0f, 0f, angle);
        }
    }

#if DEBUG
    internal class WorldToScreenDebugger : MonoBehaviour
    {
        private int width;
        private int height;

        private Vector3 opticCenterScreenPosition;
        private Vector3 opticCenterScreenOffset;

        private Vector3 opticCameraOffset;
        private Vector3 opticScreenPoint;

        private Rect windowRect;

        protected void Awake()
        {
            width = 0;
            height = 0;
            opticCenterScreenPosition = Vector3.zero;
            opticCenterScreenOffset = Vector3.zero;
            opticCameraOffset = Vector3.zero;
            opticScreenPoint = Vector3.zero;

            windowRect = new(20, 20, 300, 10);
        }

        public void UpdateData(int width, int height, Vector3 opticPos, Vector3 opticOffset,
            Vector3 cameraOffset, Vector3 screenPoint)
        {
            this.width = width;
            this.height = height;
            opticCenterScreenPosition = opticPos;
            opticCenterScreenOffset = opticOffset;
            opticCameraOffset = cameraOffset;
            opticScreenPoint = screenPoint;
        }

        protected void OnGUI()
        {
            GUILayout.BeginArea(windowRect);
            GUILayout.BeginVertical();

            windowRect = GUILayout.Window(1, windowRect, DrawWindow, "WTS Debugger");

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        private void DrawWindow(int windowId)
        {
            GUILayout.Label($"Width: {width}");
            GUILayout.Label($"Height: {height}");
            GUILayout.Label($"CenterScreenPos: {opticCenterScreenPosition}");
            GUILayout.Label($"CenterScreenOffset: {opticCenterScreenOffset}");
            GUILayout.Label($"CameraOffset: {opticCameraOffset}");
            GUILayout.Label($"ScreenPoint: {opticScreenPoint}");

            GUI.DragWindow();
        }
    }
#endif
}
