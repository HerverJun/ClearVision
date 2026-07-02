# G10A SpatialContextV1 Sidecar ADR

Status: Accepted for G10A.

## Context

Studio 2.0 now needs a shared way to describe image-space, ROI-local, undistorted, and 2D world coordinate frames. The contract must support crop propagation in G10B and later PixelToWorld projection work without changing image memory ownership.

## Options

1. Extend `ImageWrapper` with mutable spatial metadata.
   - Pros: metadata travels with images automatically.
   - Cons: mixes business coordinate state into a ref-counted Mat/byte lifecycle object, creates hidden mutation ownership, complicates copy-on-write semantics, and risks extra Mat copies for metadata propagation.

2. Use a separate `SpatialContextV1` sidecar.
   - Pros: keeps image bytes/Mat lifecycle unchanged, allows explicit output/artifact identity binding, supports immutable composition and validation, and can be attached by execution/preview code without persisting project assets.
   - Cons: callers must propagate the sidecar explicitly.

Decision: use the sidecar. `ImageWrapper` remains only an image lifecycle wrapper.

## Contract

- `FrameRefV1` identifies a frame and unit.
- Supported frame kinds are `ImageFull`, `RoiLocal`, `Undistorted`, and `World2D`.
- Supported units are `px`, `mm`, and `unitless`.
- Legacy images without metadata default to `ImageFull / px`.
- A 2D transform is a finite 3x3 homogeneous matrix with direction: `targetPoint = T(sourcePoint)`.
- Composition order is explicit: composing `A->B` then `B->C` yields `A->C` with matrix `T_BC * T_AB`.
- Singular matrices fail closed on inverse.
- Unitless frames only compose with unitless frames. Pixel/mm transforms are allowed only for calibrated image/world relationships.

## Identity

`SpatialContextBindingV1` is a sidecar binding, not persistence authority. It can record:

- project id
- source operator id
- output port id or output name
- optional run id
- preview debug session id
- preview client request sequence
- preview flow revision
- preview artifact id

Artifact ids are stored as opaque safe tokens. The sidecar does not import Desktop preview artifact types and does not read or own artifact bytes.

## Non-Goals

- No project asset persistence.
- No ROI pixel algorithm changes.
- No PixelToWorld execution in G10A.
- No mutable metadata on `ImageWrapper`.
