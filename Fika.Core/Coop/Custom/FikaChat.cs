﻿using Comfort.Common;
using EFT;
using EFT.InputSystem;
using EFT.UI;
using Fika.Core.Coop.Utils;
using Fika.Core.Networking;
using HarmonyLib;
using LiteNetLib.Utils;
using System.Reflection;
using UnityEngine;

namespace Fika.Core.Coop.Custom
{
    internal class FikaChat : MonoBehaviour
    {
        private Rect windowRect;
        private string nickname;
        private string chatText;
        private string textField;
        private string textFieldName;
        private bool show;
        private bool selectText;
        private bool isServer;
        private NetDataWriter writer;
        private UISoundsWrapper soundsWrapper;
        private InputManager manager;

        private static readonly FieldInfo showMouseField = typeof(InputManager).GetField("bool_2",
            BindingFlags.Instance | BindingFlags.NonPublic);

        protected void Awake()
        {
            windowRect = new(20, Screen.height - 260, 600, 250);
            nickname = FikaBackendUtils.PMCName;
            chatText = string.Empty;
            textField = string.Empty;
            textFieldName = "TextField";
            show = false;
            isServer = FikaBackendUtils.IsServer;
            writer = new();
            GUISounds guiSounds = Singleton<GUISounds>.Instance;
            soundsWrapper = Traverse.Create(guiSounds).Field<UISoundsWrapper>("uisoundsWrapper_0").Value;
            GameObject managerObj = GameObject.Find("___Input");
            if (managerObj != null)
            {
                InputManager inputManager = managerObj.GetComponent<InputManager>();
                if (inputManager != null)
                {
                    manager = inputManager;
                }
                else
                {
                    FikaGlobals.LogError("Could not find InputManager on the manager object");
                }
                return;
            }
            FikaGlobals.LogError("Could not find ___Input game object");
        }

        protected void Update()
        {
            if (Input.GetKeyDown(FikaPlugin.ChatKey.Value.MainKey))
            {
                ToggleVisibility();
            }
        }

        protected void OnGUI()
        {
            if (!show)
            {
                return;
            }

            GUI.skin.label.alignment = TextAnchor.LowerLeft;
            GUI.skin.window.alignment = TextAnchor.UpperLeft;
            GUI.Window(0, windowRect, DrawWindow, "Fika Chat");

            if (Event.current.isKey)
            {
                if (Event.current.keyCode is KeyCode.Return or KeyCode.KeypadEnter && show)
                {
                    SendMessage();
                    GUI.UnfocusWindow();
                    GUI.FocusControl(null);
                }
            }
        }

        private void ToggleVisibility()
        {
            show = !show;

            if (show)
            {
                if (manager != null)
                {
                    GamePlayerOwner.SetIgnoreInput(true);
                    showMouseField.SetValue(manager, true);
                }
                selectText = true;
            }
            else
            {
                if (manager != null)
                {
                    GamePlayerOwner.SetIgnoreInput(false);
                    GamePlayerOwner.MyPlayer.MovementContext.IsAxesIgnored = false;
                }
                showMouseField.SetValue(manager, false);
            }
        }

        private void SendMessage()
        {
            if (!show)
            {
                return;
            }

            if (!string.IsNullOrEmpty(textField))
            {
                string message = textField;
                textField = string.Empty;

                if (message.Length > 100)
                {
                    message = message.Substring(0, 100);
                }

                TextMessagePacket packet = new(nickname, message);
                writer.Reset();

                if (isServer)
                {
                    Singleton<FikaServer>.Instance.SendDataToAll(ref packet, LiteNetLib.DeliveryMethod.ReliableOrdered);
                }
                else
                {
                    Singleton<FikaClient>.Instance.SendData(ref packet, LiteNetLib.DeliveryMethod.ReliableOrdered);
                }
                AudioClip outgoingClip = soundsWrapper.GetSocialNetworkClip(ESocialNetworkSoundType.OutgoingMessage);
                Singleton<GUISounds>.Instance.PlaySound(outgoingClip);
                AddMessage(nickname + ": " + message);
            }
        }

        public void ReceiveMessage(string nickname, string message)
        {
            AudioClip incomingClip = soundsWrapper.GetSocialNetworkClip(ESocialNetworkSoundType.IncomingMessage);
            Singleton<GUISounds>.Instance.PlaySound(incomingClip);
            AddMessage(nickname + ": " + message);
        }

        private void AddMessage(string message)
        {
            chatText = string.Concat(chatText, message, "\n");
            if (chatText.Length > 1000)
            {
                chatText = chatText.Substring(500);
            }
        }

        private void DrawWindow(int windowId)
        {
            Rect rect = new(5, 15, 580, 200);
            GUI.Label(rect, chatText);
            rect.y += rect.height;
            Rect textRect = new(rect.x, rect.y, rect.width - 55, 25);
            GUI.SetNextControlName(textFieldName);
            textField = GUI.TextField(textRect, textField);
            if (selectText)
            {
                GUI.FocusControl(textFieldName);
                selectText = false;
            }
            rect.x += 535;
            Rect buttonRect = new(rect.x, rect.y, 50, 25);
            if (GUI.Button(buttonRect, "SEND"))
            {
                SendMessage();
                GUI.UnfocusWindow();
                GUI.FocusControl(null);
            }
        }
    }
}
