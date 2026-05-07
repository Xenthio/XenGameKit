# XenGameKit

A modular, layered base for making games in s&box. Built to restore the quality-of-life, detail, and polish that s&box used to have in its entity system — bringing together the best of the unrestricted scene system with the ease and familiarity of Source Engine modding.

## Goals

- Restore the niceties, detail, and QoL that s&box used to have in the entity system
- Build a base of polished essentials for making games
- Feel familiar to people who make stuff for the sandbox gamemode, so new modders have a familiar environment
- Bridge the gap for Source Engine modders, making them feel at home in s&box's scene system (GameObject/Component)

## Project Structure

XenGameKit is separated into tiers for separation of concerns. Each tier is designed to be independently usable.

### Tier 1 — Base FPS/TPS Movement
Basic walking around. Provided by the XMovement library (lives in this repo). Source-engine-feel movement: air strafing, step climbing, crouch, swim, ladders.

### Tier 2 — Base Game Stuff
The essentials: weapon system, health/armour, fall damage, interaction, and prop pickup.

### Tier 3 — Novelty Useful Things
The goodies: fire, doors, buttons, explosions, water, vehicles, wind/rain, and other fun functionality.

### Tier 4 — Multiplayer Gamerules
Inspired by GMod gamemode creation and GoldSrc/Source multiplayer modding. Covers spectator, teams, scoreboard, base gamerules, rounds, and more.

### Tier 5 — NPC Stuff
A detailed NPC framework with the character and depth that GoldSrc and Source had: squadding, flanking, relationship tables, sound reactions. Comes last because it should be done last.

### Future — Entity System Library
A proper entity system library that sits on top of s&box's GameObject/Component architecture — bringing TargetName, I/O, keyvalues, and the familiar Hammer entity model into the scene system cleanly. This is a significant undertaking and will be its own library when the time comes.
