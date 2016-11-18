using System;
using System.Collections.Generic;
using System.Linq;
using Stateless;

namespace DocumentApprove
{
    class Program
    {
        static void Main(string[] args)
        {
            var document = CreateDocument();

            var sm = DefineStateMachine(document);
            
            RunStateMachine(sm, document);

            //printing state machine graph
            //can be viewed by http://www.webgraphviz.com/
            Console.WriteLine();
            Console.WriteLine(sm.ToDotGraph());
        }

        private static Document CreateDocument()
        {
            var document = new Document
            {
                Owner = "owner",
                InternalApproveRequests = new List<ApproveRequest>
                {
                    new ApproveRequest {ApproverEmail = "adam@internal.com"},
                    new ApproveRequest {ApproverEmail = "ben@internal.com"},
                    new ApproveRequest {ApproverEmail = "cyan@internal.com"}
                },
                ExternalApproveRequests = new List<ApproveRequest>
                {
                    new ApproveRequest {ApproverEmail = "anna@external.com"},
                    new ApproveRequest {ApproverEmail = "bella@external.com"},
                    new ApproveRequest {ApproverEmail = "cyntia@external.com"}
                }
            };
            return document;
        }

        private static StateMachine<DocumentStatus, Trigger> DefineStateMachine(Document document)
        {
            var sm = new StateMachine<DocumentStatus, Trigger>(
                () => document.Status,
                s => document.Status = s);

            sm.Configure(DocumentStatus.Draft)
                .Permit(Trigger.CompleteDraft, DocumentStatus.PendingInternalApproval);

            sm.Configure(DocumentStatus.PendingInternalApproval)
                .PermitReentryIf(Trigger.Approve, () => document.NeedInternalApprove, "Not the last approver")
                .PermitIf(Trigger.Approve, DocumentStatus.PendingExternalApproval, () => !document.NeedInternalApprove, "Last approver")
                .Permit(Trigger.Reject, DocumentStatus.Rejected)
                .OnEntry(t => SendInternalApproveNotification(t, document), nameof(SendInternalApproveNotification));

            sm.Configure(DocumentStatus.PendingExternalApproval)
                .PermitReentryIf(Trigger.Approve, () => document.NeedExternalApprove, "Not the last approver")
                .PermitIf(Trigger.Approve, DocumentStatus.PendingInvoiceNumber, () => !document.NeedExternalApprove && document.NeedInvoiceNumber, "Last approver, invoice number not provided")
                .PermitIf(Trigger.Approve, DocumentStatus.Completed, () => !document.NeedExternalApprove && !document.NeedInvoiceNumber, "Last approver, invoice number provided")
                .Permit(Trigger.Reject, DocumentStatus.Rejected)
                .OnEntry(t => SendExternalApproveNotification(t, document), nameof(SendExternalApproveNotification));

            sm.Configure(DocumentStatus.PendingInvoiceNumber)
                .Permit(Trigger.ProvideInvoiceNumber, DocumentStatus.Completed)
                .Permit(Trigger.Reject, DocumentStatus.Rejected)
                .OnEntry(t => SendPendingInvoiceNumberNotification(t, document), nameof(SendPendingInvoiceNumberNotification));

            sm.Configure(DocumentStatus.Completed)
                .OnEntry(t => SendCompletedNotification(t, document), nameof(SendCompletedNotification));

            sm.Configure(DocumentStatus.Rejected)
                .OnEntry(t => SendRejectedNotification(t, document), nameof(SendRejectedNotification));

            return sm;
        }

        private static void RunStateMachine(StateMachine<DocumentStatus, Trigger> sm, Document document)
        {
            //completing draft
            sm.Activate();
            sm.Fire(Trigger.CompleteDraft);

            //approving
            foreach (var approveRequest in document.InternalApproveRequests.Union(document.ExternalApproveRequests))
            {
                approveRequest.IsApproved = true;
                sm.Fire(Trigger.Approve);
            }

            //providing invoice number
            document.InvoiceNumber = "123";
            sm.Fire(Trigger.ProvideInvoiceNumber);
        }

        private static void SendInternalApproveNotification(StateMachine<DocumentStatus, Trigger>.Transition transition, Document document)
        {
            Console.WriteLine($"Email sent: Dear internal approver '{document.NextApprover}', please approve or reject");
        }

        private static void SendExternalApproveNotification(StateMachine<DocumentStatus, Trigger>.Transition transition, Document document)
        {
            Console.WriteLine($"Email sent: Dear external approver '{document.NextApprover}', please approve or reject");
        }

        private static void SendPendingInvoiceNumberNotification(StateMachine<DocumentStatus, Trigger>.Transition transition, Document document)
        {
            Console.WriteLine($"Email sent: Dear '{document.ExternalApproveRequests.Last().ApproverEmail}', please provide invoice number!");
        }

        private static void SendRejectedNotification(StateMachine<DocumentStatus, Trigger>.Transition transition, Document document)
        {
            Console.WriteLine($"Email sent: Dear '{document.Owner}', document was REJECTED!");
        }

        private static void SendCompletedNotification(StateMachine<DocumentStatus, Trigger>.Transition transition, Document document)
        {
            Console.WriteLine($"Email sent: Dear '{document.Owner}', document was COMPLETED!");
        }

        private static void LogTransition(StateMachine<DocumentStatus, Trigger>.Transition t)
        {
            Console.WriteLine();
            Console.WriteLine($"{t.Source} - ({t.Trigger}) -> {t.Destination}");
        }
    }

    public enum Trigger
    {
        CompleteDraft,
        Approve,
        Reject,
        ProvideInvoiceNumber
    }

    public enum DocumentStatus
    {
        Draft,
        PendingInternalApproval,
        PendingExternalApproval,
        PendingInvoiceNumber,
        Completed,
        Rejected
    }

    public class Document
    {
        public string Owner { get; set; }
        public List<ApproveRequest> InternalApproveRequests { get; set; } = new List<ApproveRequest>();
        public List<ApproveRequest> ExternalApproveRequests { get; set; } = new List<ApproveRequest>();

        public string InvoiceNumber { get; set; }

        public DocumentStatus Status { get; set; }

        public bool NeedInternalApprove => InternalApproveRequests.Any(x => !x.IsApproved);
        public bool NeedExternalApprove => ExternalApproveRequests.Any(x => !x.IsApproved);
        public bool NeedInvoiceNumber => InvoiceNumber == null;

        public string NextApprover => InternalApproveRequests.Union(ExternalApproveRequests).FirstOrDefault(x => !x.IsApproved)?.ApproverEmail;

    }

    public class ApproveRequest
    {
        public string ApproverEmail { get; set; }
        public bool IsApproved { get; set; }
    }
}
