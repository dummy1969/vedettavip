// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

namespace VedettaVip.Shared.Contracts;

/// <summary>Tipo di dispositivo (salvato come testo): decide l'icona predefinita sulla mappa. Other in fondo (ordine dei menu).</summary>
public enum DeviceType { Router, Switch, AccessPoint, Server, Firewall, Storage, Pc, Printer, Camera, Phone, Ups, Other }

public enum SnmpVersion { None, V1, V2c, V3 }

/// <summary>Un nodo rappresenta un dispositivo, una sottomappa o un elemento statico (es. "Internet").</summary>
public enum MapNodeKind { Device, Submap, Static }

public enum EventSeverity { Info, Warning, Error, Critical }
