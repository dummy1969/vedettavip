// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

namespace VedettaVip.Shared.Contracts;

/// <summary>Tipo di dispositivo (salvato come testo): decide l'icona predefinita sulla mappa. Other in fondo (ordine dei menu).</summary>
public enum DeviceType { Router, Switch, AccessPoint, Server, Firewall, Storage, Pc, Printer, Camera, Phone, Ups, Other }

/// <summary>
/// Produttore del dispositivo (salvato come testo): abilita le azioni specifiche, es. "Apri con WinBox" per i MikroTik.
/// Generic = qualsiasi altro; Fritz = AVM FRITZ!Box. Nuovi valori in fondo: l'ordine è quello dei menu.
/// </summary>
public enum DeviceVendor { Generic, MikroTik, Cisco, DrayTek, Fritz, TpLink }

/// <summary>Accesso web di gestione del dispositivo (pagina del router): nessuno, HTTP o HTTPS.</summary>
public enum WebAccess { None, Http, Https }

/// <summary>Accesso da terminale del dispositivo, aperto con PuTTY: nessuno, SSH o Telnet.</summary>
public enum TerminalAccess { None, Ssh, Telnet }

public enum SnmpVersion { None, V1, V2c, V3 }

/// <summary>Un nodo rappresenta un dispositivo, una sottomappa o un elemento statico (es. "Internet").</summary>
public enum MapNodeKind { Device, Submap, Static }

public enum EventSeverity { Info, Warning, Error, Critical }
