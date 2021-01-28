﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using XNode;

[NodeWidth(304)]
public class EventNode : DialogNode
{
    [Input(backingValue = ShowBackingValue.Never)] public DialogNode input;
    [Output(backingValue = ShowBackingValue.Never)] public DialogNode output;
    public Sprite npcImage;
    [TextArea(4, 4)]public string message;
    [Output(dynamicPortList=true)] public string[] choices; 

	protected override void Init()
    {
		base.Init();
        DialogType = Dialog.Event;
	}

	public override object GetValue(NodePort port)
    {
		return null;
	}
}

