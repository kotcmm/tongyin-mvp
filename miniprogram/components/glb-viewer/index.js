// wx.xr-frame scene: GLB loading and native orbit/pinch controls.
Component({
  properties: {
    modelUrl: {type:String,value:''},
    renderWidth: {type:Number,value:960},
    renderHeight: {type:Number,value:840},
    modelScale: {type:String,value:'1 1 1'}
  },
  methods: {
    handleSceneReady(event) {
      this.scene = event.detail && event.detail.value;
    },
    handleProgress() {},
    handleAssetsLoaded() {
      this.triggerEvent('modelloaded');
    }
  },
  lifetimes: {
    detached() {this.scene = null}
  }
})
