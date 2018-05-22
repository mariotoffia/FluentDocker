using System;
using System.Collections.Generic;
using System.Linq;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Services;

namespace Ductus.FluentDocker.Builders
{
  public sealed class VolumeBuilder : BaseBuilder<IVolumeService>
  {
    private readonly List<string> _labels = new List<string>();
    private readonly Dictionary<string, string> _options = new Dictionary<string, string>();
    private string _driver;
    private string _name;
    private bool _removeOnDispose;
    private bool _reuseIfExist;

    public VolumeBuilder(IBuilder parent, string name = null) : base(parent)
    {
      _name = name;
    }

    public override IVolumeService Build()
    {
      var host = FindHostService();
      if (!host.HasValue)
        throw new FluentDockerException(
          $"Cannot build volume {_name} since no host service is defined");

      if (_reuseIfExist)
      {
        var volume = host.Value.GetVolumes().FirstOrDefault(x => x.Name == _name);
        if (null != volume) return volume;
      }

      return host.Value.CreateVolume(_name, _driver, 0 == _labels.Count ? null : _labels.ToArray(),
        0 == _options.Count ? null : _options, _removeOnDispose);
    }

    public VolumeBuilder WithName(string name)
    {
      _name = name;
      return this;
    }

    public VolumeBuilder UseingDriver(string driver)
    {
      _driver = driver;
      return this;
    }

    public VolumeBuilder RemoveOnDispose()
    {
      _removeOnDispose = true;
      return this;
    }

    public VolumeBuilder ReuseIfExist()
    {
      _reuseIfExist = true;
      return this;
    }

    public VolumeBuilder UseLabel(params string[] label)
    {
      if (null != label && 0 != label.Length)
      {
        _labels.AddRange(label);
      }
      return this;
    }

    public VolumeBuilder UseOption(params string[] nameValue)
    {
      if (null == nameValue || 0 == nameValue.Length)
      {
        return this;
      }

      foreach(var s in nameValue)
      {
        var splt = s.Split('=');
        if (splt.Length < 2 || string.IsNullOrEmpty(splt[0]) || string.IsNullOrEmpty(splt[1]))
        {
          continue;
        }
        _options.Add(splt[0], splt[1]);
      }
      return this;
    }

    protected override IBuilder InternalCreate()
    {
      return new VolumeBuilder(this);
    }
  }
}