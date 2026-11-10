---------------------------- MODULE LeaveLocks ----------------------------
(* One employee's leave requests under concurrent HTTP handlers.            *)
(* Each handler is one Postgres transaction: optional advisory lock on the  *)
(* employee id, a re-read of the rows its checks use, then the write.       *)
(* Without the lock, other handlers can commit between the read and write.  *)
EXTENDS Naturals, FiniteSets

CONSTANTS
  SubmitLocks,        \* app/api/workspace/route.ts POST
  DecideLocks,        \* app/api/workspace/route.ts PATCH
  CancelLocks,        \* app/api/cancellations/route.ts POST
  DecideCancelLocks,  \* app/api/cancellations/route.ts PATCH
  Allowance           \* requests (each one unit of leave) the balance allows

Req == {"r1", "r2"}

\* Concurrent HTTP requests: two submits, three approvals from two HR tabs, one
\* rejection, two cancellation requests and two cancellation approvals.
Procs == {"S1", "S2", "A1", "A2", "A3", "J1", "C1", "C2", "D1", "D2"}
Op == [p \in Procs |->
         CASE p \in {"S1", "S2"} -> "submit"
           [] p \in {"A1", "A2", "A3"} -> "approve"
           [] p = "J1" -> "reject"
           [] p \in {"C1", "C2"} -> "cancel"
           [] p \in {"D1", "D2"} -> "decideCancel"]
Target == [p \in Procs |-> IF p \in {"S2", "A3", "J1"} THEN "r2" ELSE "r1"]

Locks(p) == CASE Op[p] = "submit" -> SubmitLocks
              [] Op[p] \in {"approve", "reject"} -> DecideLocks
              [] Op[p] = "cancel" -> CancelLocks
              [] Op[p] = "decideCancel" -> DecideCancelLocks

VARIABLES status, pendingCancels, decisions, cancelDecisions, lock, pc, snap
vars == <<status, pendingCancels, decisions, cancelDecisions, lock, pc, snap>>

Held == Cardinality({r \in Req : status[r] \in {"Pending", "Approved"}})
ApprovedCount == Cardinality({r \in Req : status[r] = "Approved"})

Init ==
  /\ status = [r \in Req |-> "None"]
  /\ pendingCancels = [r \in Req |-> 0]
  /\ decisions = [r \in Req |-> 0]
  /\ cancelDecisions = [r \in Req |-> 0]
  /\ lock = "free"
  /\ pc = [p \in Procs |-> "start"]
  /\ snap = [p \in Procs |-> [st |-> "None", held |-> 0, approved |-> 0, pend |-> 0]]

\* pg_advisory_xact_lock(employee id), held until the transaction ends.
Acquire(p) ==
  /\ pc[p] = "start" /\ Locks(p) /\ lock = "free"
  /\ lock' = p
  /\ pc' = [pc EXCEPT ![p] = "read"]
  /\ UNCHANGED <<status, pendingCancels, decisions, cancelDecisions, snap>>

NoLock(p) ==
  /\ pc[p] = "start" /\ ~Locks(p)
  /\ pc' = [pc EXCEPT ![p] = "read"]
  /\ UNCHANGED <<status, pendingCancels, decisions, cancelDecisions, lock, snap>>

\* The handler's findUnique / validateLeaveEntries reads.
Read(p) ==
  /\ pc[p] = "read"
  /\ snap' = [snap EXCEPT ![p] = [st |-> status[Target[p]], held |-> Held,
                                   approved |-> ApprovedCount,
                                   pend |-> pendingCancels[Target[p]]]]
  /\ pc' = [pc EXCEPT ![p] = "write"]
  /\ UNCHANGED <<status, pendingCancels, decisions, cancelDecisions, lock>>

\* Checks use the snapshot; Prisma writes by id. A failed check rolls back.
Write(p) ==
  LET r == Target[p]
      s == snap[p]
  IN
  /\ pc[p] = "write"
  /\ CASE Op[p] = "submit" ->
            IF s.held + 1 <= Allowance
            THEN /\ status' = [status EXCEPT ![r] = "Pending"]
                 /\ UNCHANGED <<pendingCancels, decisions, cancelDecisions>>
            ELSE UNCHANGED <<status, pendingCancels, decisions, cancelDecisions>>
       [] Op[p] = "approve" ->
            IF s.st = "Pending" /\ s.approved + 1 <= Allowance
            THEN /\ status' = [status EXCEPT ![r] = "Approved"]
                 /\ decisions' = [decisions EXCEPT ![r] = @ + 1]
                 /\ UNCHANGED <<pendingCancels, cancelDecisions>>
            ELSE UNCHANGED <<status, pendingCancels, decisions, cancelDecisions>>
       [] Op[p] = "reject" ->
            IF s.st = "Pending"
            THEN /\ status' = [status EXCEPT ![r] = "Rejected"]
                 /\ decisions' = [decisions EXCEPT ![r] = @ + 1]
                 /\ UNCHANGED <<pendingCancels, cancelDecisions>>
            ELSE UNCHANGED <<status, pendingCancels, decisions, cancelDecisions>>
       [] Op[p] = "cancel" ->
            IF s.st = "Approved" /\ s.pend = 0
            THEN /\ pendingCancels' = [pendingCancels EXCEPT ![r] = @ + 1]
                 /\ UNCHANGED <<status, decisions, cancelDecisions>>
            ELSE UNCHANGED <<status, pendingCancels, decisions, cancelDecisions>>
       [] Op[p] = "decideCancel" ->
            IF s.pend > 0 /\ s.st = "Approved"
            THEN /\ status' = [status EXCEPT ![r] = "Cancelled"]
                 /\ pendingCancels' = [pendingCancels EXCEPT ![r] = IF @ > 0 THEN @ - 1 ELSE 0]
                 /\ cancelDecisions' = [cancelDecisions EXCEPT ![r] = @ + 1]
                 /\ UNCHANGED decisions
            ELSE UNCHANGED <<status, pendingCancels, decisions, cancelDecisions>>
  /\ lock' = IF lock = p THEN "free" ELSE lock
  /\ pc' = [pc EXCEPT ![p] = "done"]
  /\ UNCHANGED snap

Next == \E p \in Procs : Acquire(p) \/ NoLock(p) \/ Read(p) \/ Write(p)
Spec == Init /\ [][Next]_vars

\* Held (pending + approved) leave never exceeds the allowance.
NoOverspend == Held <= Allowance
\* Each request gets one decision: one reviewer, one pair of decision emails.
DecidedOnce == \A r \in Req : decisions[r] <= 1
\* At most one pending cancellation per request.
OnePendingCancellation == \A r \in Req : pendingCancels[r] <= 1
\* A cancellation is approved once, so its hours come back once.
CancelledOnce == \A r \in Req : cancelDecisions[r] <= 1

Edges == {<<"None", "Pending">>, <<"Pending", "Approved">>,
          <<"Pending", "Rejected">>, <<"Approved", "Cancelled">>}
\* Statuses only move forward: a cancelled or rejected request never returns.
LegalTransitions ==
  [][\A r \in Req : status'[r] # status[r] => <<status[r], status'[r]>> \in Edges]_vars
=============================================================================
