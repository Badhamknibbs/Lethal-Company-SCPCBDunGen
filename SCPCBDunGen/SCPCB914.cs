﻿using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Networking;
using GameNetcodeStuff;
using Unity.Netcode;
using System.Runtime.CompilerServices;
using System.ComponentModel;
using System.Collections;
using System.Linq;
using SCPCBDunGen;
using System.Reflection;
using UnityEngine.UIElements.Collections;

namespace SCPCBDunGen
{
    public class SCP914Converter : NetworkBehaviour
    {
        public BoxCollider InputStore;
        public Collider colliderOutput;

        public InteractTrigger SettingKnobTrigger;
        public GameObject SettingKnobPivot;
        public AudioSource SettingKnobSoundSrc;

        public InteractTrigger ActivateTrigger;
        public AudioSource ActivateAudioSrc;

        public AudioSource RefineAudioSrc;
        public Animator DoorIn;
        public Animator DoorOut;

        public enum SCP914Setting
        {
            ROUGH = 0,
            COARSE = 1,
            ONETOONE = 2,
            FINE = 3,
            VERYFINE = 4
        }

        private readonly ValueTuple<SCP914Setting, float>[] SCP914SettingRotations =
        [
            (SCP914Setting.ROUGH, 90),
            (SCP914Setting.COARSE, 45),
            (SCP914Setting.ONETOONE, 0),
            (SCP914Setting.FINE, -45),
            (SCP914Setting.VERYFINE, -90)
        ];

        private Dictionary<Item, List<Item>>[] arItemMappings =
        [
            new Dictionary<Item, List<Item>>(), // ROUGH
            new Dictionary<Item, List<Item>>(), // COARSE
            new Dictionary<Item, List<Item>>(), // ONETOONE
            new Dictionary<Item, List<Item>>(), // FINE
            new Dictionary<Item, List<Item>>()  // VERYFINE
        ];
        
        private Dictionary<EnemyType, List<EnemyType>>[] arEnemyMappings =
        [
            new Dictionary<EnemyType, List<EnemyType>>(), // ROUGH
            new Dictionary<EnemyType, List<EnemyType>>(), // COARSE
            new Dictionary<EnemyType, List<EnemyType>>(), // ONETOONE
            new Dictionary<EnemyType, List<EnemyType>>(), // FINE
            new Dictionary<EnemyType, List<EnemyType>>()  // VERYFINE
        ];

        private Dictionary<Item, List<EnemyType>>[] arItemEnemyMappings =
        [
            new Dictionary<Item, List<EnemyType>>(), // ROUGH
            new Dictionary<Item, List<EnemyType>>(), // COARSE
            new Dictionary<Item, List<EnemyType>>(), // ONETOONE
            new Dictionary<Item, List<EnemyType>>(), // FINE
            new Dictionary<Item, List<EnemyType>>()  // VERYFINE
        ];

        private Dictionary<EnemyType, List<Item>>[] arEnemyItemMappings =
        [
            new Dictionary<EnemyType, List<Item>>(), // ROUGH
            new Dictionary<EnemyType, List<Item>>(), // COARSE
            new Dictionary<EnemyType, List<Item>>(), // ONETOONE
            new Dictionary<EnemyType, List<Item>>(), // FINE
            new Dictionary<EnemyType, List<Item>>()  // VERYFINE
        ];

        private int iCurrentState = 0;
        private bool bActive = false; // Server parameter to reject multiple activation at once
        private Transform ScrapTransform;
        private RoundManager roundManager;
        private StartOfRound StartOfRound;
        private EnemyType MaskedType;

        public void AddConversion(SCP914Setting setting, Item itemInput, List<Item> lItemOutputs) {
            int iSetting = (int)setting;
            Dictionary<Item, List<Item>> dItemMapping = arItemMappings[iSetting];
            // If dictionary item already exists, concatenate the array
            if (dItemMapping.TryGetValue(itemInput, out List<Item> lExisting)) {
                lExisting.AddRange(lItemOutputs);
            } else {
                arItemMappings[iSetting].Add(itemInput, lItemOutputs);
            }
        }

        public void AddConversion(SCP914Setting setting, EnemyType enemyInput, List<EnemyType> lEnemyOutputs)
        {
            int iSetting = (int)setting;
            Dictionary<EnemyType, List<EnemyType>> dItemMapping = arEnemyMappings[iSetting];
            // If dictionary item already exists, concatenate the array
            if (dItemMapping.TryGetValue(enemyInput, out List<EnemyType> lExisting))
            {
                lExisting.AddRange(lEnemyOutputs);
            } else
            {
                arEnemyMappings[iSetting].Add(enemyInput, lEnemyOutputs);
            }
        }

        public void AddConversion(SCP914Setting setting, Item itemInput, List<EnemyType> lEnemyOutputs) {
            int iSetting = (int)setting;
            Dictionary<Item, List<EnemyType>> dItemMapping = arItemEnemyMappings[iSetting];
            // If dictionary item already exists, concatenate the array
            if (dItemMapping.TryGetValue(itemInput, out List<EnemyType> lExisting)) {
                lExisting.AddRange(lEnemyOutputs);
            } else {
                arItemEnemyMappings[iSetting].Add(itemInput, lEnemyOutputs);
            }
        }

        public void AddConversion(SCP914Setting setting, EnemyType enemyInput, List<Item> lItemOutputs) {
            int iSetting = (int)setting;
            Dictionary<EnemyType, List<Item>> dItemMapping = arEnemyItemMappings[iSetting];
            // If dictionary item already exists, concatenate the array
            if (dItemMapping.TryGetValue(enemyInput, out List<Item> lExisting)) {
                lExisting.AddRange(lItemOutputs);
            } else {
                arEnemyItemMappings[iSetting].Add(enemyInput, lItemOutputs);
            }
        }

        private Dictionary<Item, List<Item>> GetItemMapping() { return arItemMappings[iCurrentState]; }

        private Dictionary<EnemyType, List<EnemyType>> GetEnemyMapping() { return arEnemyMappings[iCurrentState]; }

        private Dictionary<Item, List<EnemyType>> GetItemEnemyMapping() { return arItemEnemyMappings[iCurrentState]; }

        private Dictionary<EnemyType, List<Item>> GetEnemyItemMapping() { return arEnemyItemMappings[iCurrentState]; }

        private Vector3 GetRandomNavMeshPositionInCollider(Collider collider) {
            Vector3 vPosition = collider.bounds.center;
            // Since the room can be rotated, and extents don't take into account rotation, we instead make a smallest cube fit for the navmesh location
            float fExtentsMin = Math.Min(collider.bounds.extents.x, collider.bounds.extents.z);
            vPosition.x += UnityEngine.Random.Range(-fExtentsMin, fExtentsMin);
            vPosition.z += UnityEngine.Random.Range(-fExtentsMin, fExtentsMin);
            vPosition.y -= collider.bounds.extents.y / 2;
            NavMeshHit navHit;
            if (NavMesh.SamplePosition(vPosition, out navHit, 5, -1)) return navHit.position;
            else return vPosition; // Failsafe in case navmesh search fails
        }

        private Vector3 GetRandomPositionInCollider(Collider collider) {
            Vector3 vPosition = collider.bounds.center;
            // Since the room can be rotated, and extents don't take into account rotation, we instead make a smallest cube fit for the items to spawn in
            float fExtentsMin = Math.Min(collider.bounds.extents.x, collider.bounds.extents.z);
            vPosition.x += UnityEngine.Random.Range(-fExtentsMin, fExtentsMin);
            vPosition.z += UnityEngine.Random.Range(-fExtentsMin, fExtentsMin);
            return vPosition;
        }

        [ServerRpc(RequireOwnership = false)]
        public void TurnStateServerRpc() {
            // Update the state for all clients to the next one in the array
            int iNextState = (iCurrentState + 1) % 5;
            TurnStateClientRpc(iNextState);
        }

        [ClientRpc]
        public void TurnStateClientRpc(int iNewState) {
            iCurrentState = iNewState;
            Vector3 vCurrentRot = SettingKnobPivot.transform.rotation.eulerAngles;
            vCurrentRot.z = SCP914SettingRotations[iCurrentState].Item2;
            SettingKnobPivot.transform.rotation = Quaternion.Euler(vCurrentRot);
            SettingKnobSoundSrc.Play();
        }

        [ServerRpc(RequireOwnership = false)]
        public void ActivateServerRpc() {
            if (bActive) return;
            bActive = true;
            ActivateClientRpc();
            StartCoroutine(ConversionProcess());
        }

        [ClientRpc]
        public void ActivateClientRpc() {
            ActivateTrigger.interactable = false;
            SettingKnobTrigger.interactable = false;
            ActivateAudioSrc.Play();
            DoorIn.SetBoolString("open", false);
            DoorOut.SetBoolString("open", false);
        }

        [ClientRpc]
        public void RefineFinishClientRpc() {
            ActivateTrigger.interactable = true;
            SettingKnobTrigger.interactable = true;
            DoorIn.SetBoolString("open", true);
            DoorOut.SetBoolString("open", true);
        }

        [ClientRpc]
        public void SpawnItemsClientRpc(NetworkObjectReference[] arNetworkObjectReferences, int[] arScrapValues, bool bChargeBattery) {
            for (int i = 0; i < arNetworkObjectReferences.Length; i++) {
                SCPCBDunGen.Logger.LogInfo($"Item conversion scrap value {i}: {arScrapValues[i]}");
                if (arNetworkObjectReferences[i].TryGet(out NetworkObject networkObject)) {
                    GrabbableObject component = networkObject.GetComponent<GrabbableObject>();
                    if (component.itemProperties.isScrap) component.SetScrapValue(arScrapValues[i]);
                    if (component.itemProperties.requiresBattery) component.insertedBattery.charge = bChargeBattery ? 1.0f : 0.0f;
                }
            }
        }

        // The main meat function for converting everything (server side)
        IEnumerator ConversionProcess() {
            RefineAudioSrc.Play();
            yield return new WaitForSeconds(7); // Initial wait before collecting item data so doors can close

            if (roundManager == null) {
                SCPCBDunGen.Logger.LogInfo("Getting round manager");
                roundManager = FindObjectOfType<RoundManager>();
                if (roundManager == null) {
                    SCPCBDunGen.Logger.LogError("Failed to find round manager.");
                    yield break;
                }
            }

            if (ScrapTransform == null) {
                ScrapTransform = GameObject.FindGameObjectWithTag("MapPropsContainer")?.transform;
                if (ScrapTransform == null) {
                    SCPCBDunGen.Logger.LogError("SCPCB Failed to find props container.");
                    yield break;
                }
            }

            List<NetworkObjectReference> lNetworkObjectReferences = new List<NetworkObjectReference>();
            List<int> lScrapValues = new List<int>();
            bool bChargeBatteries = (iCurrentState > 1);

            BoxCollider InputCollider = InputStore;
            if (InputCollider == null) {
                SCPCBDunGen.Logger.LogError("Failed to get input store collider.");
                yield break;
            }

            Collider[] inputObjects = Physics.OverlapBox(InputCollider.center + InputCollider.gameObject.transform.position, InputCollider.size / 2);

            SCPCBDunGen.Logger.LogInfo($"Contained item count: {inputObjects.Length}");
            foreach (Collider collider in inputObjects) {
                GameObject gameObject = collider.gameObject;
                SCPCBDunGen.Logger.LogInfo("Converting " + gameObject.name);
                GrabbableObject grabbable = gameObject.GetComponent<GrabbableObject>();
                // If grabbable item, convert it
                if (grabbable != null) {
                    SCPCBDunGen.Logger.LogInfo("Converting item.");
                    ConvertItem(lNetworkObjectReferences, lScrapValues, grabbable);
                    continue;
                }
                // Special case for players
                PlayerControllerB playerController = gameObject.GetComponent<PlayerControllerB>();
                if (playerController != null) {
                    SCPCBDunGen.Logger.LogInfo("Converting player.");
                    ConvertPlayer(playerController);
                    continue;
                }
                // If enemy, convert it
                EnemyAI enemy = gameObject.GetComponentInParent<EnemyAI>();
                if (enemy != null) {
                    SCPCBDunGen.Logger.LogInfo("Converting enemy.");
                    ConvertEnemy(lNetworkObjectReferences, lScrapValues, enemy);
                    continue;
                }
                SCPCBDunGen.Logger.LogWarning($"Unidentified object: {gameObject.name}. Not converting.");
            }
            SCPCBDunGen.Logger.LogInfo("Finished spawning scrap, syncing with clients");
            SpawnItemsClientRpc(lNetworkObjectReferences.ToArray(), lScrapValues.ToArray(), bChargeBatteries);
            yield return new WaitForSeconds(7); // 14 seconds (7 * 2) is the duration of the refining SFX (at the part where the bell dings is when we open the doors)
            RefineFinishClientRpc();
            bActive = false;
        }

        // ** Convert Items functions
        private void ConvertItemToItem(List<NetworkObjectReference> lNetworkObjectReferences, List<int> lScrapValues, GrabbableObject grabbable) {
            Vector3 vPosition = GetRandomPositionInCollider(colliderOutput);
            GameObject? gameObjectCreated = null;
            NetworkObject? networkObject = null;
            GrabbableObject? grabbableObject = null;
            if (GetItemMapping().TryGetValue(grabbable.itemProperties, out List<Item> lItemOutputs)) {
                SCPCBDunGen.Logger.LogInfo("Mapping found");
                Item? itemOutput = lItemOutputs[roundManager.AnomalyRandom.Next(lItemOutputs.Count)];
                if (itemOutput != null) {
                    SCPCBDunGen.Logger.LogInfo("Conversion found");
                    gameObjectCreated = Instantiate(itemOutput.spawnPrefab, vPosition, Quaternion.identity, ScrapTransform);
                    networkObject = gameObjectCreated.GetComponent<NetworkObject>();
                    grabbableObject = gameObjectCreated.GetComponent<GrabbableObject>();
                }
            } else {
                SCPCBDunGen.Logger.LogInfo("No conversion, making new item copy with new scrap value");
                // No conversion mapping found, just create a new item copy
                gameObjectCreated = Instantiate(grabbable.itemProperties.spawnPrefab, vPosition, Quaternion.identity, ScrapTransform);
                networkObject = gameObjectCreated.GetComponent<NetworkObject>();
                grabbableObject = gameObjectCreated.GetComponent<GrabbableObject>();
            }
            SCPCBDunGen.Logger.LogInfo("Preprocessing done");
            // If the grabbable object is null, return here and don't spawn an output
            if (grabbableObject == null) {
                SCPCBDunGen.Logger.LogInfo("Conversion was null, item is intended to be destroyed in process.");
                return;
            }
            Item itemCreated = grabbableObject.itemProperties;

            // Post processing for items created
            if (itemCreated.isScrap) {
                SCPCBDunGen.Logger.LogInfo("Item is scrap or null, generating a copy with new value");
                GrabbableObject grabbableCreated = gameObjectCreated.GetComponent<GrabbableObject>();
                // Generate scrap value
                int iScrapValue = (int)(roundManager.AnomalyRandom.Next(itemCreated.minValue, itemCreated.maxValue) * roundManager.scrapValueMultiplier);
                grabbableCreated.SetScrapValue(iScrapValue);
                SCPCBDunGen.Logger.LogInfo($"new scrap value: {iScrapValue}");
                lScrapValues.Add(iScrapValue);
            } else {
                SCPCBDunGen.Logger.LogInfo("Item is not scrap, adding empty scrap value");
                lScrapValues.Add(0);
            }
            networkObject.Spawn(destroyWithScene: true);
            lNetworkObjectReferences.Add(networkObject);
        }
        
        private void ConvertItemToEnemy(GrabbableObject grabbable, EnemyType enemyType) {
            // Position to put the enemy
            Vector3 NavPosition = GetRandomNavMeshPositionInCollider(colliderOutput);
            roundManager.SpawnEnemyGameObject(NavPosition, 0, -1, enemyType);
        }

        private void ConvertItem(List<NetworkObjectReference> lNetworkObjectReferences, List<int> lScrapValues, GrabbableObject grabbable) {
            if (grabbable.isHeld) return; // Best not disturb items in players' inventories, TODO implement conversion of held items
            SCPCBDunGen.Logger.LogInfo($"Found grabbable item {grabbable.itemProperties.name}");
            Dictionary<Item, List<Item>> dcItemMappings = GetItemMapping();
            Dictionary<Item, List<EnemyType>> dcItemEnemyMappings = GetItemEnemyMapping();

            if (dcItemEnemyMappings.TryGetValue(grabbable.itemProperties, out List<EnemyType> lEnemyTypes)) {
                // Do the coinflip before trying to get the value as it's faster than the dictionary get
                if ((roundManager.AnomalyRandom.Next(2) == 0) && dcItemMappings.ContainsKey(grabbable.itemProperties)) {
                    ConvertItemToItem(lNetworkObjectReferences, lScrapValues, grabbable);
                } else ConvertItemToEnemy(grabbable, lEnemyTypes[roundManager.AnomalyRandom.Next(lEnemyTypes.Count)]);
            } else ConvertItemToItem(lNetworkObjectReferences, lScrapValues, grabbable); // This function handles no conversions being present

            // Regardless of outcome, destroy the item
            Destroy(grabbable.gameObject);
        }
        
        // ** Convert Players (Teleport)
        [ClientRpc]
        private void ConvertPlayerTeleportClientRpc(NetworkBehaviourReference netBehaviourRefPlayer, Vector3 vPosition) {
            NetworkBehaviour netBehaviourPlayer = null;
            netBehaviourRefPlayer.TryGet(out netBehaviourPlayer);
            if (netBehaviourPlayer == null) {
                SCPCBDunGen.Logger.LogError("Failed to get player controller.");
                return;
            }
            PlayerControllerB playerController = (PlayerControllerB)netBehaviourPlayer;
            playerController.TeleportPlayer(vPosition);
        }

        [ClientRpc]
        private void ConvertPlayerKillClientRpc(NetworkBehaviourReference netBehaviourRefPlayer) {
            NetworkBehaviour netBehaviourPlayer = null;
            netBehaviourRefPlayer.TryGet(out netBehaviourPlayer);
            if (netBehaviourPlayer == null) {
                SCPCBDunGen.Logger.LogError("Failed to get player controller.");
                return;
            }
            PlayerControllerB playerController = (PlayerControllerB)netBehaviourPlayer;
            playerController.KillPlayer(Vector3.zero, causeOfDeath: CauseOfDeath.Mauling);
        }

        // ** Convert Players (Coarse & Fine, health change)
        [ClientRpc]
        private void ConvertPlayerAlterHealthClientRpc(NetworkBehaviourReference netBehaviourRefPlayer, int iHealthDelta) {
            NetworkBehaviour netBehaviourPlayer = null;
            netBehaviourRefPlayer.TryGet(out netBehaviourPlayer);
            if (netBehaviourPlayer == null) {
                SCPCBDunGen.Logger.LogError("Failed to get player controller.");
                return;
            }
            PlayerControllerB playerController = (PlayerControllerB)netBehaviourPlayer;
            playerController.DamagePlayer(iHealthDelta, causeOfDeath: CauseOfDeath.Crushing);
        }

        // ** Convert Players (1:1, skin change)
        [ClientRpc]
        private void ConvertPlayerRandomSkinClientRpc(NetworkBehaviourReference netBehaviourRefPlayer, int iSuitID) {
            if (StartOfRound == null) {
                StartOfRound = FindObjectOfType<StartOfRound>();
                if (StartOfRound == null) {
                    SCPCBDunGen.Logger.LogError("Failed to find StartOfRound.");
                    return;
                }
            }
            NetworkBehaviour netBehaviourPlayer = null;
            netBehaviourRefPlayer.TryGet(out netBehaviourPlayer);
            if (netBehaviourPlayer == null) {
                SCPCBDunGen.Logger.LogError("Failed to get player controller.");
                return;
            }
            PlayerControllerB playerController = (PlayerControllerB)netBehaviourPlayer;

            int iIndex = 0;
            foreach (UnlockableItem unlockable in StartOfRound.unlockablesList.unlockables) {
                if (unlockable.suitMaterial != null) {
                    SCPCBDunGen.Logger.LogInfo($"Found suit at index {iIndex}");
                }
                iIndex++;
            }

            // Iterate all suits until we run out of suit IDs
            UnlockableItem unlockableItem = StartOfRound.unlockablesList.unlockables[iSuitID];
            if (unlockableItem == null) {
                SCPCBDunGen.Logger.LogError($"Invalid suit ID: {iSuitID}");
                return;
            }
            Material material = unlockableItem.suitMaterial;
            playerController.thisPlayerModel.material = material;
            playerController.thisPlayerModelLOD1.material = material;
            playerController.thisPlayerModelLOD2.material = material;
            playerController.thisPlayerModelArms.material = material;
            playerController.currentSuitID = iSuitID;
        }

        private void ConvertPlayerRandomSkin(PlayerControllerB playerController) {
            if (StartOfRound == null) {
                StartOfRound = FindObjectOfType<StartOfRound>();
                if (StartOfRound == null) {
                    SCPCBDunGen.Logger.LogError("Failed to find StartOfRound.");
                    return;
                }
            }
            List<int> lSuitIDs = new List<int>();
            int iIndex = 0;
            foreach (UnlockableItem unlockable in StartOfRound.unlockablesList.unlockables) {
                // Skip if not a suit or if it's a suit the player already has equipped
                if ((unlockable.suitMaterial != null) && (iIndex != playerController.currentSuitID)) {
                    lSuitIDs.Add(iIndex);
                }
                iIndex++;
            }
            int iCount = lSuitIDs.Count;
            if (iCount == 0) {
                SCPCBDunGen.Logger.LogError("No suits to swap to found.");
                return; // No suits to switch
            }
            int iSuitID = roundManager.AnomalyRandom.Next(0, iCount); // Assuming all player have the same suits/suit mods, this should represent the same suit for everyone
            NetworkBehaviourReference netBehaviourRefPlayer = playerController;
            ConvertPlayerRandomSkinClientRpc(netBehaviourRefPlayer, lSuitIDs[iSuitID]);
        }

        // ** Convert Players (Very Fine, masked)
        private IEnumerator ConvertPlayerMaskedWaitForSpawn(NetworkObjectReference netObjRefMasked, NetworkBehaviourReference netBehaviourRefPlayer) {
            NetworkObject netObjMasked = null;
            NetworkBehaviour netBehaviourPlayer = null;
            float fStartTime = Time.realtimeSinceStartup;
            yield return new WaitUntil(() => Time.realtimeSinceStartup - fStartTime > 20.0f || netObjRefMasked.TryGet(out netObjMasked));
            yield return new WaitUntil(() => Time.realtimeSinceStartup - fStartTime > 20.0f || netBehaviourRefPlayer.TryGet(out netBehaviourPlayer));
            PlayerControllerB playerController = (PlayerControllerB)netBehaviourPlayer;
            if (playerController.deadBody == null) {
                yield return new WaitUntil(() => Time.realtimeSinceStartup - fStartTime > 20.0f || playerController.deadBody != null);
            }
            if (playerController.deadBody != null) {
                playerController.deadBody.DeactivateBody(false);
                if (netObjMasked != null) {
                    MaskedPlayerEnemy maskedPlayerEnemy = netObjMasked.GetComponent<MaskedPlayerEnemy>();
                    maskedPlayerEnemy.mimickingPlayer = playerController;
                    maskedPlayerEnemy.SetSuit(playerController.currentSuitID);
                    maskedPlayerEnemy.SetEnemyOutside(false);
                    playerController.redirectToEnemy = maskedPlayerEnemy;
                }
            }
        }

        [ClientRpc]
        private void ConvertPlayerMaskedClientRpc(NetworkObjectReference netObjRefMasked, NetworkBehaviourReference netBehaviourRefPlayer) {
            NetworkBehaviour netBehaviourPlayer = null;
            netBehaviourRefPlayer.TryGet(out netBehaviourPlayer);
            if (netBehaviourPlayer == null) {
                SCPCBDunGen.Logger.LogError("Failed to get player controller.");
                return;
            }
            PlayerControllerB playerController = (PlayerControllerB)netBehaviourPlayer;
            playerController.KillPlayer(Vector3.zero, true, CauseOfDeath.Suffocation);
            if (playerController.deadBody != null) {
                playerController.deadBody.DeactivateBody(setActive: false);
            }
            StartCoroutine(ConvertPlayerMaskedWaitForSpawn(netObjRefMasked, netBehaviourRefPlayer));
        }

        private void ConvertPlayerMasked(NetworkBehaviourReference netBehaviourRefPlayer, Vector3 vMaskedPosition) {
            NetworkBehaviour netBehaviourPlayer = null;
            netBehaviourRefPlayer.TryGet(out netBehaviourPlayer);
            if (netBehaviourPlayer == null) {
                SCPCBDunGen.Logger.LogError("Failed to get player controller.");
                return;
            }
            PlayerControllerB playerController = (PlayerControllerB)netBehaviourPlayer;

            playerController.KillPlayer(Vector3.zero, true, CauseOfDeath.Suffocation);
            if (StartOfRound == null) {
                StartOfRound = FindObjectOfType<StartOfRound>();
                if (StartOfRound == null) {
                    SCPCBDunGen.Logger.LogError("Failed to find StartOfRound.");
                    return;
                }
            }
            if (MaskedType == null) {
                SelectableLevel selectableLevel = Array.Find(StartOfRound.levels, x => x.PlanetName == "8 Titan"); // Pivot off Titan to get masked enemy data
                if (selectableLevel == null) {
                    SCPCBDunGen.Logger.LogError("Failed to get Titan level data.");
                    return;
                }
                MaskedType = selectableLevel.Enemies.Find(x => x.enemyType.enemyName == "Masked")?.enemyType; // Find masked player enemy
                if (MaskedType == null) {
                    SCPCBDunGen.Logger.LogError("Failed to get masked enemy type.");
                    return;
                }
            }
            NetworkObjectReference netObjMasked = roundManager.SpawnEnemyGameObject(vMaskedPosition, 0, -1, MaskedType);
            if (netObjMasked.TryGet(out var networkObject)) {
                SCPCBDunGen.Logger.LogInfo("Got network object for mask enemy");
                MaskedPlayerEnemy maskedPlayerEnemy = networkObject.GetComponent<MaskedPlayerEnemy>();
                maskedPlayerEnemy.SetSuit(playerController.currentSuitID);
                maskedPlayerEnemy.mimickingPlayer = playerController;
                maskedPlayerEnemy.SetEnemyOutside(false); // 914 is only ever in facility so we can make this assumption
                playerController.redirectToEnemy = maskedPlayerEnemy;
                if (playerController.deadBody != null) {
                    playerController.deadBody.DeactivateBody(setActive: false);
                }
            }
            ConvertPlayerMaskedClientRpc(netObjMasked, netBehaviourRefPlayer);
        }

        // ** Convert Players picker
        private void ConvertPlayer(PlayerControllerB playerController) {
            SCPCBDunGen.Logger.LogInfo("Player detected, doing player conversion");
            SCP914Setting Setting = (SCP914Setting)iCurrentState;
            // First, regardless of what we do, teleport player into the output area
            Vector3 vPlayerPosition = GetRandomNavMeshPositionInCollider(colliderOutput);
            NetworkBehaviourReference netBehaviourPlayer = playerController;
            ConvertPlayerTeleportClientRpc(netBehaviourPlayer, vPlayerPosition);
            switch (Setting) {
                case SCP914Setting.ROUGH: // Kill player
                    ConvertPlayerKillClientRpc(netBehaviourPlayer);
                    break;
                case SCP914Setting.COARSE: // Deal 50 damage to player (can kill)
                    ConvertPlayerAlterHealthClientRpc(netBehaviourPlayer, 50);
                    break;
                case SCP914Setting.ONETOONE: // Change skin of player to a random other one
                    ConvertPlayerRandomSkin(playerController);
                    break;
                case SCP914Setting.FINE: // Heal player for 50 health
                    ConvertPlayerAlterHealthClientRpc(netBehaviourPlayer, -50);
                    break;
                case SCP914Setting.VERYFINE: // Convert player to a masked
                    if (!playerController.AllowPlayerDeath()) {
                        SCPCBDunGen.Logger.LogInfo("Refined player with Very Fine, but player death is prevented. Doing nothing.");
                        break;
                    }
                    ConvertPlayerMasked(playerController, vPlayerPosition);
                    break;
                default:
                    SCPCBDunGen.Logger.LogError("Invalid SCP 914 setting when attempting to convert player.");
                    break;
            }
        }

        // ** Enemy conversion functions
        [ClientRpc]
        private void TeleportEnemyClientRpc(NetworkBehaviourReference netBehaviourRefEnemy, Vector3 vPosition) {
            NetworkBehaviour netBehaviourEnemy = null;
            netBehaviourRefEnemy.TryGet(out netBehaviourEnemy);
            if (netBehaviourEnemy == null) {
                SCPCBDunGen.Logger.LogError("Failed to get enemy AI.");
                return;
            }
            EnemyAI enemyAI = (EnemyAI)netBehaviourEnemy;
            enemyAI.serverPosition = vPosition;
        }

        private void MoveEnemy(EnemyAI enemy, Vector3 NavPosition) {
            SCPCBDunGen.Logger.LogInfo($"No conversions for enemy: {enemy.enemyType.enemyName}. Teleporting to {NavPosition}");
            NetworkBehaviourReference netBehaviourEnemy = enemy;
            TeleportEnemyClientRpc(netBehaviourEnemy, NavPosition);
            enemy.agent.Warp(NavPosition);
            enemy.SyncPositionToClients();
            // If this was on rough, kill them (if possible)
            if (iCurrentState == 0) enemy.KillEnemyOnOwnerClient();
        }

        private void ConvertEnemyToEnemy(EnemyAI enemy) {
            Dictionary<EnemyType, List<EnemyType>> dcCurrentMapping = GetEnemyMapping();
            // Position to put the enemy
            Vector3 NavPosition = GetRandomNavMeshPositionInCollider(colliderOutput);
            do { // While false for break-out
                if (!dcCurrentMapping.TryGetValue(enemy.enemyType, out List<EnemyType> enemyOutputTypes)) break;
                if (enemyOutputTypes.Count == 0) break;
                // We have an enemy we can convert this into
                EnemyType enemyTargetType = enemyOutputTypes[roundManager.AnomalyRandom.Next(enemyOutputTypes.Count)];
                roundManager.SpawnEnemyGameObject(NavPosition, 0, -1, enemyTargetType);
                Destroy(enemy.gameObject);
            } while (false);
            // No conversion found, teleport enemy
            MoveEnemy(enemy, NavPosition);
        }

        private void ConvertEnemyToItem(List<NetworkObjectReference> lNetworkObjectReferences, List<int> lScrapValues, EnemyAI enemy) {
            Vector3 vPosition = GetRandomPositionInCollider(colliderOutput);

            GameObject? gameObjectCreated = null;
            NetworkObject? networkObject = null;
            GrabbableObject? grabbableObject = null;
            if (GetEnemyItemMapping().TryGetValue(enemy.enemyType, out List<Item> lItemOutputs)) {
                SCPCBDunGen.Logger.LogInfo("Mapping found");
                Item? itemOutput = lItemOutputs[roundManager.AnomalyRandom.Next(lItemOutputs.Count)];
                Destroy(enemy.gameObject);
                if (itemOutput != null) {
                    SCPCBDunGen.Logger.LogInfo("Conversion found");
                    gameObjectCreated = Instantiate(itemOutput.spawnPrefab, vPosition, Quaternion.identity, ScrapTransform);
                    networkObject = gameObjectCreated.GetComponent<NetworkObject>();
                    grabbableObject = gameObjectCreated.GetComponent<GrabbableObject>();
                }
            } else {
                SCPCBDunGen.Logger.LogInfo("No conversion, teleporting enemy");
                // Position to put the enemy
                Vector3 NavPosition = GetRandomNavMeshPositionInCollider(colliderOutput);
                MoveEnemy(enemy, NavPosition);
            }
            SCPCBDunGen.Logger.LogInfo("Preprocessing done");
            // If the grabbable object is null, return here and don't spawn an output
            if (grabbableObject == null) {
                SCPCBDunGen.Logger.LogInfo("Conversion was null, item is intended to be destroyed in process.");
                return;
            }
            Item itemCreated = grabbableObject.itemProperties;

            // Post processing for items created
            if (itemCreated.isScrap) {
                SCPCBDunGen.Logger.LogInfo("Item is scrap or null, generating a copy with new value");
                GrabbableObject grabbableCreated = gameObjectCreated.GetComponent<GrabbableObject>();
                // Generate scrap value
                int iScrapValue = (int)(roundManager.AnomalyRandom.Next(itemCreated.minValue, itemCreated.maxValue) * roundManager.scrapValueMultiplier);
                grabbableCreated.SetScrapValue(iScrapValue);
                SCPCBDunGen.Logger.LogInfo($"new scrap value: {iScrapValue}");
                lScrapValues.Add(iScrapValue);
            } else {
                SCPCBDunGen.Logger.LogInfo("Item is not scrap, adding empty scrap value");
                lScrapValues.Add(0);
            }
            networkObject.Spawn(destroyWithScene: true);
            lNetworkObjectReferences.Add(networkObject);
        }

        private void ConvertEnemy(List<NetworkObjectReference> lNetworkObjectReferences, List<int> lScrapValues, EnemyAI enemy) {
            Dictionary<EnemyType, List<Item>> dcEnemyItemMappings = GetEnemyItemMapping();
            Dictionary<EnemyType, List<EnemyType>> dcEnemyMappings = GetEnemyMapping();

            if (GetEnemyMapping().TryGetValue(enemy.enemyType, out List<EnemyType> lEnemyMappings)) {
                if ((roundManager.AnomalyRandom.Next(2) == 0) && GetEnemyItemMapping().ContainsKey(enemy.enemyType)) ConvertEnemyToItem(lNetworkObjectReferences, lScrapValues, enemy);
                else ConvertEnemyToEnemy(enemy);
            } else if (GetEnemyItemMapping().ContainsKey(enemy.enemyType)) {
                ConvertEnemyToItem(lNetworkObjectReferences, lScrapValues, enemy);
            } else {
                // No mapping, teleport enemy to exit
                Vector3 NavPosition = GetRandomNavMeshPositionInCollider(colliderOutput);
                MoveEnemy(enemy, NavPosition);
            }

            // Finally destroy the enemy
            Destroy(enemy.gameObject);
        }
    }
}