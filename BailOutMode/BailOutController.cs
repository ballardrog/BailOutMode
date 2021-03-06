﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Playables;
using HarmonyLib;
using TMPro;
using Zenject;

namespace BailOutMode
{
    internal class BailOutController : MonoBehaviour
    {
#pragma warning disable CS0649
        [Inject]
        private readonly IMultiplayerSessionManager multiplayerSessionManager;
        [Inject]
        private readonly GameplayCoreSceneSetupData gameplayCoreSceneSetupData;
#pragma warning restore CS0649
        #region "Fields with get/setters"
        public static BailOutController instance { get; private set; }
        private TextMeshProUGUI _failText;
        private static PlayerSpecificSettings _playerSettings;
        #endregion
        #region "Fields"
        public bool isHiding = false;
        public int numFails = 0;
        private LevelFailedTextEffect LevelFailedEffect;
        private PlayableDirector LevelFailedEnergyBarAnimation;

        public bool IsEnabled
        {
            get
            {
                return Configuration.instance.IsEnabled 
                    && BS_Utils.Plugin.LevelData.Mode != BS_Utils.Gameplay.Mode.Mission
                    && !gameplayCoreSceneSetupData.gameplayModifiers.instaFail
                    && gameplayCoreSceneSetupData.gameplayModifiers.energyType == GameplayModifiers.EnergyType.Bar;
            }
        }

        public TextMeshProUGUI FailText
        {
            get
            {
                if (_failText == null)
                    _failText = CreateText("");
                return _failText;
            }
            set { _failText = value; }
        }

        private static PlayerSpecificSettings PlayerSettings
        {
            get
            {
                if (_playerSettings == null)
                {
                    _playerSettings = BS_Utils.Plugin.LevelData?.GameplayCoreSceneSetupData?.playerSpecificSettings;
                    if (_playerSettings == null)
                        Logger.log.Warn($"Unable to find PlayerSettings");
                }
                return _playerSettings;
            }
        }

        private MultiplayerLocalActivePlayerGameplayManager _multiGameplayManager;
        private MultiplayerLocalActivePlayerGameplayManager MultiGameplayManager
        {
            get
            {
                if (_multiGameplayManager == null)
                    _multiGameplayManager = FindObjectOfType<MultiplayerLocalActivePlayerGameplayManager>();
                return _multiGameplayManager;
            }
        }

        private float PlayerHeight
        {
            get
            {
                if (PlayerSettings != null)
                {
                    //Logger.Debug($"Using PlayerHeight {PlayerSettings.playerHeight}");
                    return PlayerSettings.playerHeight;
                }
                else
                {
                    Logger.log.Warn("Unable to find PlayerSettings, using 1.8 for player height");
                    return 1.8f;
                }
            }
        }

        public static bool InstanceExists
        {
            get { return instance != null; }
        }

        #endregion

        public void Awake()
        {
            //Logger.Trace("BailOutController Awake()");
            instance = this;
            isHiding = false;
        }

        public void Start()
        {
            Logger.log?.Debug("BailOutController Start()");
            if (BS_Utils.Gameplay.ScoreSubmission.Disabled)
                Logger.log?.Warn($"ScoreSubmission already disabled by {BS_Utils.Gameplay.ScoreSubmission.ModString}");
            if(IsEnabled)
                Logger.log.Info("BailOutMode enabled");
            if (multiplayerSessionManager == null)
                Logger.log?.Warn($"connectedPlayerManager is null.");
            LevelFailedEffect = GameObject.FindObjectOfType<LevelFailedTextEffect>();
            if (LevelFailedEffect == null)
                Logger.log?.Warn("Couldn't find LevelFailedTextEffect");
            LevelFailedEnergyBarAnimation = (PlayableDirector)AccessTools.Field(typeof(GameEnergyUIPanel), "_playableDirector").GetValue(GameObject.FindObjectOfType<GameEnergyUIPanel>());
        }

        private void OnDestroy()
        {
            Logger.log.Debug("Destroying BailOutController");
            instance = null;
        }
        private bool _lastStandingCheckActive;
        public void OnLevelFailed()
        {
            if (multiplayerSessionManager != null && multiplayerSessionManager.isConnected && !_lastStandingCheckActive)
                StartCoroutine(CheckLastStanding());
            if(LevelFailedEffect == null)
                LevelFailedEffect = GameObject.FindObjectOfType<LevelFailedTextEffect>();

            //Logger.Trace("BailOutController ShowLevelFailed()");
            //BS_Utils.Gameplay.ScoreSubmission.DisableSubmission(Plugin.PluginName); Don't need this here
            UpdateFailText($"Bailed Out {numFails} time{(numFails != 1 ? "s" : "")}");
            try
            {
                if (!Configuration.instance.RepeatFailEffect && numFails > 1)
                    return; // Don't want to repeatedly show fail effect, stop here.

                //Logger.Debug("Showing fail effects");
                if (!isHiding && Configuration.instance.ShowFailEffect && LevelFailedEffect != null)
                {
                    LevelFailedEffect.ShowEffect();
                    if (Configuration.instance.FailEffectDuration > 0)
                        StartCoroutine(hideLevelFailed());
                    else
                        isHiding = true; // Fail text never hides, so don't try to keep showing it
                }

                if (Configuration.instance.ShowFailAnimation && LevelFailedEnergyBarAnimation != null)
                {
                    // Cancel any in-progress fail animation before playing a new animation, to prevent missing an animation when failing multiple times in quick succession.
                    LevelFailedEnergyBarAnimation.Stop();
                    LevelFailedEnergyBarAnimation.Play();
                }

            }
            catch (Exception ex)
            {
                Logger.log.Error($"Exception trying to show the fail Effects: {ex.Message}");
                Logger.log.Debug(ex);
            }
        }
        private int lastFailDuration = Configuration.instance.FailEffectDuration;
        private WaitForSeconds failDurationWait = new WaitForSeconds(Configuration.instance.FailEffectDuration);
        public IEnumerator<WaitForSeconds> hideLevelFailed()
        {
#if DEBUG
            Logger.log.Trace("BailOutController hideLevelFailed() CoRoutine");
#endif
            int failDuration = Configuration.instance.FailEffectDuration;
            if (lastFailDuration != failDuration)
            {
                failDurationWait = new WaitForSeconds(Configuration.instance.FailEffectDuration);
                lastFailDuration = failDuration;
            }
            if (!isHiding)
            {
#if DEBUG
                Logger.log.Trace($"BailOutController, will hide LevelFailedEffect after {Configuration.instance.FailEffectDuration}s");
#endif
                isHiding = true;
                yield return failDurationWait;
#if DEBUG
                Logger.log.Trace($"BailOutController, hiding LevelFailedEffect");
#endif
                LevelFailedEffect.gameObject.SetActive(false);
                isHiding = false;
            }
#if DEBUG
            else
                Logger.log.Trace("BailOutController, skipping hideLevel because isHiding is true");
#endif
            yield break;
        }

        private WaitForSeconds _lastStandingWait = new WaitForSeconds(5);
        private IEnumerator<WaitForSeconds> CheckLastStanding()
        {
            _lastStandingCheckActive = true;
            while (true)
            {
                if (multiplayerSessionManager.connectedPlayerCount == 0)
                {
#if DEBUG
                    Logger.log?.Debug($"connectedPlayerCount is {multiplayerSessionManager.connectedPlayerCount}, skipping last standing check.");
#endif
                    yield return _lastStandingWait;
                }
                else
                {
#if DEBUG
                    Logger.log?.Debug($"connectedPlayerCount is {multiplayerSessionManager.connectedPlayerCount}. Checking if they failed.");
#endif
                    IConnectedPlayer[] players = multiplayerSessionManager.connectedPlayers.ToArray();
                    bool hasActivePlayer = false;
                    for (int i = 0; i < players.Length; i++)
                    {
                        if (!players[i].isMe && !players[i].IsFailed())
                        {
                            hasActivePlayer = true;
                        }
#if DEBUG
                        if (players[i].isMe)
                            Logger.log?.Debug($"Local player flagged as failed: {players[i].IsFailed()}");
#endif
                    }
                    if (!hasActivePlayer)
                    {
                        Logger.log?.Debug($"All other players failed, triggering level failed.");
                        if (MultiGameplayManager != null)
                        {
                            MultiGameplayManager.HandleGameEnergyDidReach0();
                        }
                        else
                            Logger.log?.Warn($"Tried to fail level, but ILevelEndActions isn't a StandardLevelGameplayManager.");
                    }
                    yield return _lastStandingWait;
                }
            }
            _lastStandingCheckActive = false;
        }

        public static void FacePosition(Transform obj, Vector3 targetPos)
        {
            var rotAngle = Quaternion.LookRotation(StringToVector3(Configuration.instance.CounterTextPosition) - targetPos);
            obj.rotation = rotAngle;
        }

        public TextMeshProUGUI CreateText(string text)
        {
            Canvas _canvas = new GameObject("BailOutFailText").AddComponent<Canvas>();
            _canvas.gameObject.transform.localScale = new Vector3(0.01f, 0.01f, 0.01f);
            _canvas.renderMode = RenderMode.WorldSpace;
            (_canvas.transform as RectTransform).sizeDelta = new Vector2(0f, 0f);
            return CreateText(_canvas, text, new Vector2(0f, 0f), (_canvas.transform as RectTransform).sizeDelta);
        }

        public TextMeshProUGUI CreateText(Canvas parent, string text, Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            GameObject gameObj = parent.gameObject;
            gameObj.SetActive(false);
            TextMeshProUGUI textMesh = gameObj.AddComponent<TextMeshProUGUI>();
            /*
            Teko-Medium SDF No Glow
            Teko-Medium SDF
            Teko-Medium SDF No Glow Fading
            */
            var font = Instantiate(Resources.FindObjectsOfTypeAll<TMP_FontAsset>().First(t => t.name == "Teko-Medium SDF No Glow"));
            if (font == null)
            {
                Logger.log.Error("Could not locate font asset, unable to display text");
                return null;
            }
            textMesh.font = font;
            textMesh.fontSize = Configuration.instance.CounterTextSize;
            textMesh.rectTransform.SetParent(parent.transform as RectTransform, false);
            textMesh.text = text;
            textMesh.color = Color.white;
            textMesh.rectTransform.anchorMin = new Vector2(0f, 0f);
            textMesh.rectTransform.anchorMax = new Vector2(0f, 0f);
            textMesh.rectTransform.sizeDelta = sizeDelta;
            textMesh.rectTransform.anchoredPosition = anchoredPosition;
            textMesh.alignment = TextAlignmentOptions.Left;
            FacePosition(textMesh.gameObject.transform, new Vector3(0, PlayerHeight, 0));
            gameObj.SetActive(true);
            return textMesh;
        }

        public static void CenterTextMesh(TextMeshProUGUI text, float playerHeight)
        {
            text.ForceMeshUpdate();
            var pos = StringToVector3(Configuration.instance.CounterTextPosition);
            pos.x = pos.x - (text.renderedWidth * text.gameObject.transform.localScale.x) / 2;
            pos.y = pos.y + (text.renderedHeight * text.gameObject.transform.localScale.y);
            FacePosition(text.gameObject.transform, new Vector3(0, playerHeight, 0));
            text.transform.position = pos;

        }

        public void UpdateFailText(string text)
        {
            FailText.text = text;
            if(FailText.fontSize != Configuration.instance.CounterTextSize)
                FailText.fontSize = Configuration.instance.CounterTextSize;
            CenterTextMesh(FailText, PlayerHeight);
        }

        public static Vector3 StringToVector3(string vStr)
        {
            string[] sAry = vStr.Split(',');
            try
            {
                Vector3 retVal = new Vector3(
                    float.Parse(sAry[0]),
                    float.Parse(sAry[1]),
                    float.Parse(sAry[2]));
                //Logger.Debug("StringToVector3: {0}={1}", vStr, retVal.ToString());
                return retVal;
            }
            catch (Exception ex)
            {
                Logger.log.Error($"Cannot convert value of {vStr} to a Vector. Needs to be in the format #,#,#: {ex.Message}");
                Logger.log.Debug(ex);
                return new Vector3(DefaultSettings.CounterTextPosition.x, DefaultSettings.CounterTextPosition.y, DefaultSettings.CounterTextPosition.z);
            }
        }
    }
}
