namespace Bella.Wms.Platform.Data;

/// <summary>
/// A unit of work against the <c>irms</c> database — the main WMS database.
/// </summary>
/// <remarks>
/// <para>
/// The two OpenEdge databases are separate connections and separate transactions. A
/// transaction cannot span them, and Phase 6 §4 rule 1 says a transaction never spans a
/// boundary anyway, so the split is enforced in the type system rather than left to a
/// configuration string.
/// </para>
/// <para>
/// The practical benefit is that a reviewer reading a constructor knows which database
/// the class touches without opening its SQL. A class asking for both is asking for a
/// distributed transaction and should be rejected in review.
/// </para>
/// </remarks>
public interface IIrmsUnitOfWork : IUnitOfWork;

/// <summary>
/// A unit of work against the <c>wmscomm</c> database — the interface message queue
/// holding <c>comm_in</c> and <c>comm_out</c>.
/// </summary>
public interface IWmsCommUnitOfWork : IUnitOfWork;
