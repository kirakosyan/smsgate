using System;
using System.Collections.Generic;
using System.Linq;

namespace Smpp
{
    public abstract partial class Connection
    {
        private readonly record struct MultipartKey(int Reference, byte ReferenceKind, string Sender,
            string Recipient, string Format, Common.CommandId Command);

        private sealed class MultipartAssembly
        {
            internal MultipartKey Key;
            internal int Expected;
            internal readonly Dictionary<int, MultipartSlot> Parts = new();
            internal int Pending;
            internal DateTime IdleSince;
            internal LinkedListNode<MultipartAssembly> Expiry;
        }

        private sealed class MultipartSlot
        {
            internal MultipartAssembly Assembly;
            internal Common.MultipartMessage Part;
            internal long Bytes;
            internal string AcceptedId;
            internal int ListIndex = -1;
        }

        private readonly Dictionary<MultipartKey, MultipartAssembly> assemblies = new();
        private readonly LinkedList<MultipartAssembly> idleAssemblies = new();
        private readonly HashSet<MultipartSlot> reservedSegments = new();
        // Parallel indices keep the protected legacy list available without scanning it.
        private readonly List<MultipartSlot> acceptedSegments = new();
        private long multipartBytes;
        private int maxMultipartAssemblies = 1024;
        private int maxMultipartBytes = 8 * 1024 * 1024;

        /// <summary>Maximum incomplete inbound multipart messages per connection, including pending application decisions.</summary>
        public int MaxMultipartAssemblies
        {
            get { lock (SessionLock) return maxMultipartAssemblies; }
            set { Guard.ThrowIfNegativeOrZero(value); lock (SessionLock) maxMultipartAssemblies = value; }
        }

        /// <summary>Memory budget for retained multipart segments, including text, addresses, and per-segment bookkeeping.</summary>
        public int MaxMultipartBytes
        {
            get { lock (SessionLock) return maxMultipartBytes; }
            set { Guard.ThrowIfNegativeOrZero(value); lock (SessionLock) maxMultipartBytes = value; }
        }

        // All reassembly operations are serialized by SessionLock. A reservation pins an
        // assembly while the application decides and is released on rejection or disconnect.
        private MessageAcknowledgement? ReserveSegment(Common.MultipartMessage part, out MultipartSlot slot)
        {
            slot = null;
            if (part.num_of_parts < 1 || part.num_of_parts > 255 || part.part_num < 1 || part.part_num > part.num_of_parts)
                return MessageAcknowledgement.Reject(1); // ESME_RINVMSGLEN

            ExpireMultipart(DateTime.UtcNow);
            var key = new MultipartKey(part.reference, part.ReferenceKind, part.sender, part.recipient, part.body_format, part.command);
            assemblies.TryGetValue(key, out var assembly);
            if (assembly != null)
            {
                if (assembly.Expected != part.num_of_parts) return MessageAcknowledgement.Reject(1);
                if (assembly.Parts.TryGetValue(part.part_num, out var previous))
                {
                    if (previous.Part.short_message != part.short_message) return MessageAcknowledgement.Reject(1);
                    return previous.AcceptedId == null ? MessageAcknowledgement.Reject(0x58) :
                        MessageAcknowledgement.Accept(previous.AcceptedId);
                }
            }

            // Include room for the maximum 64-character acknowledgement ID and bookkeeping.
            long bytes = 256L + 2L * (part.short_message.Length + part.sender.Length + part.recipient.Length + part.body_format.Length);
            if (multipartBytes + bytes > maxMultipartBytes || (assembly == null && assemblies.Count >= maxMultipartAssemblies))
                return MessageAcknowledgement.Reject(0x58);
            if (assembly == null)
            {
                assembly = new MultipartAssembly { Key = key, Expected = part.num_of_parts };
                assemblies.Add(key, assembly);
            }
            if (assembly.Expiry != null)
            {
                idleAssemblies.Remove(assembly.Expiry);
                assembly.Expiry = null;
            }
            slot = new MultipartSlot { Assembly = assembly, Part = part, Bytes = bytes };
            assembly.Parts.Add(part.part_num, slot);
            assembly.Pending++;
            multipartBytes += bytes;
            reservedSegments.Add(slot);
            return null;
        }

        private string FinishSegment(MultipartSlot slot, string acceptedId)
        {
            if (!reservedSegments.Remove(slot)) return null;
            var assembly = slot.Assembly;
            assembly.Pending--;
            if (acceptedId == null)
            {
                assembly.Parts.Remove(slot.Part.part_num);
                multipartBytes -= slot.Bytes;
            }
            else
            {
                slot.AcceptedId = acceptedId;
                slot.ListIndex = acceptedSegments.Count;
                acceptedSegments.Add(slot);
                multipart_messages.Add(slot.Part);
            }
            if (assembly.Pending != 0) return null;
            if (assembly.Parts.Count == assembly.Expected)
            {
                var body = new System.Text.StringBuilder();
                for (int part = 1; part <= assembly.Expected; part++)
                    body.Append(assembly.Parts[part].Part.short_message);
                RemoveAssembly(assembly);
                return body.ToString();
            }
            if (assembly.Parts.Count == 0)
                assemblies.Remove(assembly.Key);
            else
            {
                assembly.IdleSince = DateTime.UtcNow;
                assembly.Expiry = idleAssemblies.AddLast(assembly);
            }
            return null;
        }

        private void RemoveAssembly(MultipartAssembly assembly)
        {
            assemblies.Remove(assembly.Key);
            if (assembly.Expiry != null) idleAssemblies.Remove(assembly.Expiry);
            foreach (var slot in assembly.Parts.Values)
            {
                int last = acceptedSegments.Count - 1;
                int index = slot.ListIndex;
                acceptedSegments[index] = acceptedSegments[last];
                acceptedSegments[index].ListIndex = index;
                multipart_messages[index] = multipart_messages[last];
                acceptedSegments.RemoveAt(last);
                multipart_messages.RemoveAt(last);
                multipartBytes -= slot.Bytes;
            }
        }

        internal void ExpireMultipart(DateTime now)
        {
            while (idleAssemblies.First is { } first && now - first.Value.IdleSince >= MultipartLifetime)
                RemoveAssembly(first.Value);
        }

        private void ReleaseMultipartReservations()
        {
            foreach (var slot in reservedSegments.ToArray()) FinishSegment(slot, null);
        }

        private void ClearMultipart()
        {
            assemblies.Clear();
            idleAssemblies.Clear();
            reservedSegments.Clear();
            acceptedSegments.Clear();
            multipart_messages.Clear();
            multipartBytes = 0;
        }
    }
}
