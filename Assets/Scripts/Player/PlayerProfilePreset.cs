﻿using UnityEngine;

[CreateAssetMenu(fileName="PlayerProfilePreset", menuName = "Profile/Player")]
public class PlayerProfilePreset : ScriptableObject
{
    public int happiness;
    public int money;
    public int salary;
    public bool havePet;
}

