﻿using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using XNode;

public class EventDirector : MonoBehaviour
{
    static readonly WaitForSeconds PauseProcessingWait = new WaitForSeconds(0.3f);
    static readonly WaitForSeconds DelayStartProcessing = new WaitForSeconds(0.12f);

    [Header("Event Setting")]
    [SerializeField]
    EventGraph getSalaryScenario;

    [SerializeField]
    EventGraph[] normalScenarios;

    [Header("InnerTime")]
    [SerializeField]
    InnerTime innerTime;

    [Header("UI Setting")]
    [SerializeField]
    AlertBoxController alertbox;

    [SerializeField]
    UIInGameController ingameUI;

    [Header("Sound Setting")]
    [SerializeField]
    SoundManager soundManager;

    [Header("Debug")]
    [SerializeField]
    bool isInPlayground = false;

    public struct Cache
    {
        public StartNode startNode;
        public EndNode endNode;
        public MessageNode messageNode;
        public ResultNode resultNode;
        public LateResultNode lateResultNode;
        public RandomNode randomNode;
        public MultiRandomNode multiRandomNode;
        public PromptNode promptNode;
        public TimeSkipNode timeSkipNode;
        public ShopNode shopNode;
    }

    public Action<EventType> OnStartScenario;
    public Action<EventType> OnFinishScenario;

    public bool IsStartScenario => isStartScenario;

    bool isStartScenario;
    bool shouldEndGame;

    bool previousPauseProcessing;
    bool shouldPauseProcessing;

    Cache cache;
    EventType currentEventType;

    Queue<EventGraph> optionalScenario;
    EventGraph[] normalEvents;

    Dictionary<DateTime, EventGraph> keyEvents; // <- key = day of year
    Dictionary<DateTime, Queue<DialogNode>> lateEventQueue; // <- key = day of year

    DialogNode currentNode;

    EventGraph currentScenario;
    EventGraph currentLateScenario;

    Coroutine processNodeCoroutine;
    WaitUntil shouldWaitForUnpauseProcessing;

    void Awake()
    {
        Initialize();
    }

    void Start()
    {
        SubscribeEvent();
    }

    void OnDestroy()
    {
        UnsubscribeEvent();
    }

    void Initialize()
    {
        cache = new Cache();
        lateEventQueue = new Dictionary<DateTime, Queue<DialogNode>>();
        shouldWaitForUnpauseProcessing = new WaitUntil(() => { return (!shouldPauseProcessing); });
    }

    void SubscribeEvent()
    {
        GameController.Instance.OnGameStateChange += OnGameStateChange;
        alertbox.OnSelectButton += AlertBox_MessageSelectChoiceCallback;

        innerTime.OnMonthPass += OnMonthPass;
        innerTime.OnYearPass += OnYearPass;

        innerTime.OnFinishAdvanceTime += OnFinishAdvanceTime;

    }

    void UnsubscribeEvent()
    {
        GameController.Instance.OnGameStateChange -= OnGameStateChange;
        alertbox.OnSelectButton -= AlertBox_MessageSelectChoiceCallback;

        innerTime.OnMonthPass -= OnMonthPass;
        innerTime.OnYearPass -= OnYearPass;

        innerTime.OnFinishAdvanceTime -= OnFinishAdvanceTime;
    }

    public void BeginPlay()
    {
        if (isInPlayground)
        {
            ingameUI.ShowIngameUI();
            StartScenario(normalScenarios[0]);
        }
        else
        {
            innerTime.StartClock();
            ingameUI.ShowIngameUI();

            GameController.Instance?.BeginPlay();
            soundManager.Play();

            // logic to start the event emiiter here
        }
    }

    void OnGameStateChange(GameState state)
    {
        switch (state)
        {
            case GameState.End:
            {
                Debug.Log("Game Over...");
            }
            break;

            default:
                break;
        }
    }

    void AlertBox_MessageSelectChoiceCallback(int id)
    {
        var node = cache.messageNode;

        if (node == null) {
            Debug.LogError("Cannot find the last message node...");
            return;
        }

        bool haveCustomChoice = (node.choices.Length > 0);

        if (haveCustomChoice && (id >= node.choices.Length)) {
            Debug.Log("Unknown choices...");
            return;
        }

        string portName = "output";

        if (haveCustomChoice) {
            portName = ("choices " + id);
        }

        alertbox.Show(false);

        currentNode = currentScenario.GetNextNode(currentNode, portName);
        SetPauseProcessingState(false);
    }

    void OnFinishAdvanceTime()
    {
        currentNode = currentScenario.GetNextNode(currentNode);
        SetPauseProcessingState(false);
    }

    void OnDayPass(DateTime date)
    {
        GameController.Instance?.RemoveSalaryPerDay();
    }

    void OnMonthPass(DateTime date)
    {
        GameController.Instance?.GetSalary();
        StartScenario(getSalaryScenario);
    }

    void OnYearPass(DateTime date)
    {
        shouldEndGame = true;
    }

    void StartScenario(EventGraph scenario)
    {
        if (isStartScenario) {
            return;
        }

        currentScenario = scenario;
        currentNode = scenario.GetStartNode();

        if (currentNode == null) {
            Debug.LogError("Cannot find the scenario start node...");
            Debug.LogError("Scenario will not process...");
            return;
        }

        isStartScenario = true;
        OnStartScenario?.Invoke(currentEventType);

        innerTime.Pause();
        StartProcessNode();

        Debug.Log("Scenario has started..");
    }

    void StartLateScenario(DialogNode node)
    {
        if (isStartScenario) {
            return;
        }

        currentScenario = (EventGraph)node.graph;
        currentNode = node;

        if (currentNode == null) {
            Debug.LogError("Cannot find the scenario of late event...");
            Debug.LogError("Late event will not resume ...");
            return;
        }

        isStartScenario = true;
        OnStartScenario?.Invoke(currentEventType);

        innerTime.Pause();
        StartProcessNode();

        Debug.Log("Late Scenario has started..");
    }

    void EndScenario()
    {
        if (!isStartScenario) {
            return;
        }

        ResetScenario();
        OnFinishScenario?.Invoke(currentEventType);

        alertbox.Show(false);

        if (!isInPlayground)
        {
            innerTime.Pause(false);
        }

        Debug.Log("Scenario has finished..");

        // Hacks, game over here
        if (shouldEndGame) {
            GameController.Instance?.GameOver();
            innerTime.ResetClock();
        }
    }

    void ResetScenario()
    {
        isStartScenario = false;
        currentNode = null;
        previousPauseProcessing = false;
        shouldPauseProcessing = false;
    }

    void StartProcessNode()
    {
        if (!isStartScenario) {
            Debug.LogError("Attempt to start processing node without starting scenario...");
            return;
        }

        if (processNodeCoroutine != null) {
            StopCoroutine(processNodeCoroutine);
        }

        processNodeCoroutine = StartCoroutine(ProcessNode());
    }

    IEnumerator ProcessNode()
    {
        while (isStartScenario)
        {
            bool noNodeLeft = (currentNode == null);

            if (noNodeLeft) {
                EndScenario();
                yield break;
            }

            switch (currentNode.DialogType)
            {
                case DialogNode.Dialog.Start:
                {
                    cache.startNode = (currentNode as StartNode);
                    ProcessStartNode(cache.startNode);
                }
                break;
                
                case DialogNode.Dialog.Message:
                {
                    if (!shouldPauseProcessing) {
                        SetPauseProcessingState(true);
                    }

                    cache.messageNode = (currentNode as MessageNode);
                    ProcessMessageNode(cache.messageNode);
                }
                break;

                case DialogNode.Dialog.Prompt:
                {
                    //TODO : skip for now
                    /* if (!shouldPauseProcessing) { */
                    /*     SetPauseProcessingState(true); */
                    /* } */

                    cache.promptNode = (currentNode as PromptNode);
                    ProcessPromptNode(cache.promptNode);
                }
                break;

                case DialogNode.Dialog.TimeSkip:
                {
                    if (!shouldPauseProcessing) {
                        SetPauseProcessingState(true);
                    }

                    cache.timeSkipNode = (currentNode as TimeSkipNode);
                    ProcessTimeSkipNode(cache.timeSkipNode);
                }
                break;

                case DialogNode.Dialog.Result:
                {
                    cache.resultNode = (currentNode as ResultNode);
                    ProcessResultNode(cache.resultNode);
                }
                break;

                case DialogNode.Dialog.Random:
                {
                    currentNode = currentScenario.GetNextNodeByRandom(currentNode);
                }
                break;

                case DialogNode.Dialog.MultiRandom:
                {
                     currentNode = currentScenario.GetNextNodeByMultiRandom(currentNode);
                }
                break;

                case DialogNode.Dialog.LateResult:
                {
                    // it need to add all the events that it point to, to the late event queue
                    // then : check the output port
                    // if there is no connection, end?

                    cache.lateResultNode = (currentNode as LateResultNode);
                    ProcessLateResultNode(cache.lateResultNode);
                }
                break;

                case DialogNode.Dialog.Shop:
                {
                    cache.shopNode = (currentNode as ShopNode);
                    ProcessShopNode(cache.shopNode);
                }
                break;

                case DialogNode.Dialog.End:
                {
                    EndScenario();
                    yield break;
                }
                break;
                
                default:
                break;
            }

            if (shouldPauseProcessing) {
                yield return shouldWaitForUnpauseProcessing;
                yield return DelayStartProcessing;
            }
            else {
                yield return null;
            }
        }
    }

    void SetPauseProcessingState(bool value)
    {
        previousPauseProcessing = shouldPauseProcessing;
        shouldPauseProcessing = value;
    }

    void ProcessStartNode(StartNode node)
    {
        currentEventType = node.eventType;
        currentNode = currentScenario.GetNextNode(node, "start");
    }

    void ProcessMessageNode(MessageNode node)
    {
        alertbox.SetMessageInfo(currentEventType, node.eventImage, node.message, node.choices, node.npc);
        alertbox.Show();
    }

    void ProcessResultNode(ResultNode node)
    {
        var player = GameController.Instance.Player;
        player.EditResource(node.actions);
        currentNode = currentScenario.GetNextNode(currentNode);
    }

    void ProcessTimeSkipNode(TimeSkipNode node)
    {
        var duration = node.duration;
        var amount = node.amount;

        TimeSpan timeSpan;

        switch (duration)
        {
            case TimeSkipNode.SkipDuration.Day:
            {
                var date = innerTime.Calendar;
                var nextDate = date.AddDays(amount);
                timeSpan = (nextDate - innerTime.Calendar);
            }
            break;

            case TimeSkipNode.SkipDuration.Week:
            {
                var date = innerTime.Calendar;
                var nextDate = date.AddDays(amount * 7);
                timeSpan = (nextDate - innerTime.Calendar);
            }
            break;

            case TimeSkipNode.SkipDuration.Month:
            {
                var date = innerTime.Calendar;
                var nextDate = date.AddMonths(amount);
                timeSpan = (nextDate - innerTime.Calendar);
            }
            break;

            default:
                timeSpan = new TimeSpan(0, 0, 0, 0);
            break;
        }

        innerTime.AdvanceTime(timeSpan);
    }

    // TODO
    void ProcessLateResultNode(LateResultNode node)
    {
        for (int i = 0; i < node.infos.Length; ++i)
        {
            // add this info to the queue of late event here
            // add the pointer of event

            var info = node.infos[i];
            var nodeToAdd = currentScenario.GetNextNode(currentNode, "infos " + i);

            Debug.Log("Duration : " + info.duration);
            Debug.Log("Amount : " + info.amount);

            if (nodeToAdd == null) {
                Debug.Log("no node to add to queue");
            }
            else {
                Debug.Log("Node to add : " + nodeToAdd.ToString());
            }
        }

        //then
        currentNode = currentScenario.GetNextNode(currentNode);
    }

    void ProcessShopNode(ShopNode node)
    {
        for (int i = 0; i < node.shopCarts.Length; ++i)
        {
            // add this info to the queue of late event here
            // add the pointer of event

            var item = node.shopCarts[i];

            if (ShopNode.ShopActionType.Buy == item.actionType)
            {
                Debug.Log($"Buy {item.item.itemName}");
            }
            else if (ShopNode.ShopActionType.Sell == item.actionType)
            {
                Debug.Log($"Sell : {item.item.itemName}");
            }
            else {
                Debug.Log($"Give : {item.item.itemName}");
            }
        }

        //then
        currentNode = currentScenario.GetNextNode(currentNode);
    }

    // TODO
    void ProcessPromptNode(PromptNode node)
    {
        /* Skip */
        /* ------------------------------ */
        /* promptbox.SetPromptInfo() */
        /* promptbox.Show(); */

        /* var id = 0; */
        /* var portName = ("choices " + id); */

        /* if (node.choices.Length <= 0) { */
        /*     currentNode = null; */
        /*     return; */
        /* } */

        /* currentNode = currentScenario.GetNextNode(currentNode, portName); */
        /* ------------------------------ */

        Debug.LogError("You cannot use prompt node...");
        currentNode = currentScenario.GetNextNode(currentNode);
    }
}

