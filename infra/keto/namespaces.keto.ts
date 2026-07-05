// CS46 — Ory Permission Language (OPL) schema for the "keto" ReBAC PDP adapter.
//
// This is the Keto TRANSLATION of the shared ReBAC model (the SpiceDB `SpiceDbSchema` and the OpenFGA
// `RebacModel`): the same four fintech relationship categories over user / region / branch / customer /
// account, so Keto answers the SAME account questions as SpiceDB and OpenFGA for the SHARED seed graph
// (`RebacSeedTuples`). The head-to-head is only fair if every engine models one domain.
//
// Class names are lowercase to match the shared `RebacTypes` namespace strings ("account", "user", …)
// that the adapter writes and checks against — a Keto namespace equals its OPL class name verbatim.
//
// Relations (the `related` block) are the directly-assigned tuples; permissions (the `permits` block)
// are the computed unions/traversals. `includes(ctx.subject)` is a direct-membership match; `traverse`
// follows a relation to another object and evaluates a permission on it — the Keto equivalents of a
// SpiceDB `computedUserset` and arrow. A subject is a bare subject_id for a user or a whole-object
// subject_set (empty relation) for another object; both `.includes` and `.traverse` resolve them.
//
// can_view composes the direct grant plus all four derived paths (viewer / owner / delegate /
// customer.can_view / branch.manage); can_transact is the tighter set (transactor / owner /
// customer.can_view) — exactly the shared model's two unions.

import { Namespace, Context } from "@ory/keto-namespace-types"

class user implements Namespace {}

class region implements Namespace {
  related: {
    manager: user[]
  }

  permits = {
    manage: (ctx: Context): boolean => this.related.manager.includes(ctx.subject),
  }
}

class branch implements Namespace {
  related: {
    region: region[]
    manager: user[]
  }

  permits = {
    manage: (ctx: Context): boolean =>
      this.related.manager.includes(ctx.subject) ||
      this.related.region.traverse((r) => r.permits.manage(ctx)),
  }
}

class customer implements Namespace {
  related: {
    branch: branch[]
    relationship_manager: user[]
    viewer: user[]
  }

  permits = {
    can_view: (ctx: Context): boolean =>
      this.related.viewer.includes(ctx.subject) ||
      this.related.relationship_manager.includes(ctx.subject) ||
      this.related.branch.traverse((b) => b.permits.manage(ctx)),
  }
}

class account implements Namespace {
  related: {
    owner: (user | customer)[]
    customer: customer[]
    branch: branch[]
    delegate: user[]
    viewer: user[]
    transactor: user[]
  }

  permits = {
    can_view: (ctx: Context): boolean =>
      this.related.viewer.includes(ctx.subject) ||
      this.related.owner.includes(ctx.subject) ||
      this.related.delegate.includes(ctx.subject) ||
      this.related.customer.traverse((c) => c.permits.can_view(ctx)) ||
      this.related.branch.traverse((b) => b.permits.manage(ctx)),

    can_transact: (ctx: Context): boolean =>
      this.related.transactor.includes(ctx.subject) ||
      this.related.owner.includes(ctx.subject) ||
      this.related.customer.traverse((c) => c.permits.can_view(ctx)),
  }
}
