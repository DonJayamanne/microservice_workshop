﻿/*
 * Copyright (c) 2016 by Fred George
 * May be used freely except for training; license required for training.
 */

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MicroServiceWorkshop.RapidsRivers
{
    public class River : RapidsConnection.IMessageListener
    {
        private readonly List<IPacketListener> _listeners = new List<IPacketListener>();
        private readonly List<IValidation> _validations = new List<IValidation>();

        public River(RapidsConnection rapidsConnection)
        {
            rapidsConnection.Register(this);
        }

        public void Register(IPacketListener listener)
        {
            _listeners.Add(listener);
        }
        
        public void HandleMessage(RapidsConnection sendPort, string message)
        {
            PacketProblems problems = new PacketProblems(message);
            JObject jsonPacket = JsonPacket(message, problems);
            foreach (IValidation v in _validations)
            {
                if (problems.AreSevere()) break;
                v.Validate(jsonPacket, problems);
            }
            if (problems.HasErrors())
                OnError(sendPort, problems);
            else
                Packet(sendPort, jsonPacket, problems);
        }

        private JObject JsonPacket(string message, PacketProblems problems)
        {
            JObject result = null;
            try
            {
                result = JObject.Parse(message);
            }
            catch (JsonException)
            {
                problems.SevereError("Invalid JSON format per NewtonSoft JSON library");
            }
            catch (Exception e)
            {
                problems.SevereError("Unknown failure. HandleMessage is: " + e.Message);
            }
            return result;
        }

        private void OnError(RapidsConnection sendPort, PacketProblems errors)
        {
            foreach (IPacketListener l in _listeners) l.OnError(sendPort, errors);
        }

        private void Packet(RapidsConnection sendPort, JObject jsonPacket, PacketProblems warnings)
        {
            foreach (IPacketListener l in _listeners) l.Packet(sendPort, jsonPacket, warnings);
        }

        public River Require(params string[] jsonKeyStrings)
        {
            _validations.Add(new RequiredKeys(jsonKeyStrings));
            return this;
        }

        public River Forbid(params string[] jsonKeyStrings)
        {
            _validations.Add(new ForbiddenKeys(jsonKeyStrings));
            return this;
        }

        public interface IPacketListener
        {
            void Packet(RapidsConnection connection, JObject jsonPacket, PacketProblems warnings);
            void OnError(RapidsConnection connection, PacketProblems errors);
        }

        private interface IValidation
        {
            void Validate(JObject jsonPacket, PacketProblems problems);
        }

        private class RequiredKeys : IValidation
        {
            private readonly string[] _requiredKeys;

            internal RequiredKeys(string[] requiredKeys)
            {
                _requiredKeys = requiredKeys;
            }

            public void Validate(JObject jsonPacket, PacketProblems problems)
            {
                foreach (string key in _requiredKeys)
                    if (jsonPacket[key] == null)
                        problems.Error("Missing required key '" + key + "'");
            }
        }

        private class ForbiddenKeys : IValidation
        {
            private readonly string[] _forbiddenKeys;

            internal ForbiddenKeys(string[] forbiddenKeys)
            {
                _forbiddenKeys = forbiddenKeys;
            }

            public void Validate(JObject jsonPacket, PacketProblems problems)
            {
                foreach (string key in _forbiddenKeys)
                {
                    JToken token = jsonPacket[key];
                    // Tests as suggested by NewtonSoft recommendations
                    if ((token == null) ||
                        (token.Type == JTokenType.Array && !token.HasValues) ||
                        (token.Type == JTokenType.Object && !token.HasValues) ||
                        (token.Type == JTokenType.String && token.ToString() == String.Empty) ||
                        (token.Type == JTokenType.Null))
                        problems.Information("Forbidden key '" + key + "' does not exist");
                    else problems.Error("Forbidden key '" + key + "' actually exists");
                }
            }
        }
    }
}