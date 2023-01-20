using Castle.Windsor;

namespace Ucommerce.Extensions.DependencyInjection
{
    /// <summary>
    /// Represents a set of components and related functionality
    /// packaged together.
    /// </summary>
    public interface IModule
    {
        /// <summary>
        /// Apply components to the registry.
        /// </summary>
        /// <param name="container">Container to apply configuration to.</param>
        void Configure(IWindsorContainer container);
    }
}
