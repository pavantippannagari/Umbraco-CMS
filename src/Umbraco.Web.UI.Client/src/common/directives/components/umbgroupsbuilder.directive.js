(function() {
  'use strict';

  function GroupsBuilderDirective(contentTypeHelper, contentTypeResource, mediaTypeResource, dataTypeHelper, dataTypeResource, $filter, iconHelper, $q, $timeout) {

    function link(scope, el, attr, ctrl) {

      scope.sortingMode = false;
      scope.toolbar = [];
      scope.sortableOptionsGroup = {};
      scope.sortableOptionsProperty = {};
      scope.sortingButtonLabel = "Reorder";

      function activate() {

          setSortingOptions();

          // set placeholder property on each group
          if (scope.model.groups.length !== 0) {
            angular.forEach(scope.model.groups, function(group) {
              addInitProperty(group);
            });
          }

          // add init tab
          addInitGroup(scope.model.groups);

      }

      function setSortingOptions() {

        scope.sortableOptionsGroup = {
          distance: 10,
          tolerance: "pointer",
          opacity: 0.7,
          scroll: true,
          cursor: "move",
          placeholder: "umb-group-builder__group-sortable-placeholder",
          zIndex: 6000,
          handle: ".umb-group-builder__group-handle",
          items: ".umb-group-builder__group-sortable",
          start: function(e, ui) {
            ui.placeholder.height(ui.item.height());
          },
          stop: function(e, ui) {
            updateTabsSortOrder();
          },
        };

        scope.sortableOptionsProperty = {
          distance: 10,
          tolerance: "pointer",
          connectWith: ".umb-group-builder__properties",
          opacity: 0.7,
          scroll: true,
          cursor: "move",
          placeholder: "umb-group-builder__property_sortable-placeholder",
          zIndex: 6000,
          handle: ".umb-group-builder__property-handle",
          items: ".umb-group-builder__property-sortable",
          start: function(e, ui) {
            ui.placeholder.height(ui.item.height());
          },
          stop: function(e, ui) {
            updatePropertiesSortOrder();
          }
        };

      }

      function updateTabsSortOrder() {

        var first = true;
        var prevSortOrder = 0;

        scope.model.groups.map(function(group){

          var index = scope.model.groups.indexOf(group);

          if(group.tabState !== "init") {

            // set the first not inherited tab to sort order 0
            if(!group.inherited && first) {

              // set the first tab sort order to 0 if prev is 0
              if( prevSortOrder === 0 ) {
                group.sortOrder = 0;
              // when the first tab is inherited and sort order is not 0
              } else {
                group.sortOrder = prevSortOrder + 1;
              }

              first = false;

            } else if(!group.inherited && !first) {

                // find next group
                var nextGroup = scope.model.groups[index + 1];

                // if a groups is dropped in the middle of to groups with
                // same sort order. Give it the dropped group same sort order
                if( prevSortOrder === nextGroup.sortOrder ) {
                  group.sortOrder = prevSortOrder;
                } else {
                  group.sortOrder = prevSortOrder + 1;
                }

            }

            // store this tabs sort order as reference for the next
            prevSortOrder = group.sortOrder;

          }

        });

      }

        function filterAvailableCompositions(selectedContentType, selecting) {

            //selecting = true if the user has check the item, false if the user has unchecked the item

            var selectedContentTypeAliases = selecting ?
                //the user has selected the item so add to the current list
                _.union(scope.compositionsDialogModel.compositeContentTypes, [selectedContentType.alias]) :
                //the user has unselected the item so remove from the current list
                _.reject(scope.compositionsDialogModel.compositeContentTypes, function(i) {
                    return i === selectedContentType && selectedContentType.alias;
                });

            //get the currently assigned property type aliases - ensure we pass these to the server side filer
            var propAliasesExisting = _.filter(_.flatten(_.map(scope.model.groups, function(g) {
                return _.map(g.properties, function(p) {
                    return p.alias;
                });
            })), function (f) {
                return f !== null && f !== undefined;
            });

            //use a different resource lookup depending on the content type type
            var resourceLookup = scope.contentType === "documentType" ? contentTypeResource.getAvailableCompositeContentTypes : mediaTypeResource.getAvailableCompositeContentTypes;

            return resourceLookup(scope.model.id, selectedContentTypeAliases, propAliasesExisting).then(function (filteredAvailableCompositeTypes) {
                _.each(scope.compositionsDialogModel.availableCompositeContentTypes, function (current) {
                    //reset first 
                    current.disallow = false;
                    //see if this list item is found in the response (allowed) list
                    var found = _.find(filteredAvailableCompositeTypes, function (f) {
                        return current.alias === f.alias;
                    });
                    //disallow if the item was not found in the response (allowed) list - 
                    // BUT do not set to dissallowed if it is currently checked
                    current.disallow = (selectedContentTypeAliases.indexOf(current.alias) === -1) && (found ? false : true);
                });
            });
        }

      function updatePropertiesSortOrder() {

        angular.forEach(scope.model.groups, function(group){
          if( group.tabState !== "init" ) {
            group.properties = contentTypeHelper.updatePropertiesSortOrder(group.properties);
          }
        });

      }

      /* ---------- DELETE PROMT ---------- */

      scope.togglePrompt = function (object) {
          object.deletePrompt = !object.deletePrompt;
      };

      scope.hidePrompt = function (object) {
          object.deletePrompt = false;
      };

      /* ---------- TOOLBAR ---------- */

      scope.toggleSortingMode = function(tool) {

         scope.sortingMode = !scope.sortingMode;

         if(scope.sortingMode === true) {
            scope.sortingButtonLabel = "I'm done reordering";
         } else {
            scope.sortingButtonLabel = "Reorder";
         }

      };

      scope.openCompositionsDialog = function() {

        scope.compositionsDialogModel = {
            title: "Compositions",
            contentType: scope.model,
            compositeContentTypes: scope.model.compositeContentTypes,
            view: "views/common/overlays/contenttypeeditor/compositions/compositions.html",
            confirmSubmit: {
                title: "Warning",
                description: "Removing a composition will delete all the associated property data. Once you save the document type there's no way back, are you sure?",
                checkboxLabel: "I know what I'm doing",
                enable: true
            },
            submit: function(model, oldModel, confirmed) {

                var compositionRemoved = false;

                // check if any compositions has been removed
                for(var i = 0; oldModel.compositeContentTypes.length > i; i++) {

                    var oldComposition = oldModel.compositeContentTypes[i];

                    if(_.contains(model.compositeContentTypes, oldComposition) === false) {
                        compositionRemoved = true;
                    }

                }

                // show overlay confirm box if compositions has been removed.
                if(compositionRemoved && confirmed === false) {

                    scope.compositionsDialogModel.confirmSubmit.show = true;

                // submit overlay if no compositions has been removed
                // or the action has been confirmed
                } else {
                    
                    // make sure that all tabs has an init property
                    if (scope.model.groups.length !== 0) {
                      angular.forEach(scope.model.groups, function(group) {
                        addInitProperty(group);
                      });
                    }

                    // remove overlay
                    scope.compositionsDialogModel.show = false;
                    scope.compositionsDialogModel = null;
                }

            },
            close: function(oldModel) {

                // reset composition changes
                scope.model.groups = oldModel.contentType.groups;
                scope.model.compositeContentTypes = oldModel.contentType.compositeContentTypes;

                // remove overlay
                scope.compositionsDialogModel.show = false;
                scope.compositionsDialogModel = null;

            },
            selectCompositeContentType: function (selectedContentType) {

                //first check if this is a new selection - we need to store this value here before any further digests/async
                // because after that the scope.model.compositeContentTypes will be populated with the selected value.
                var newSelection = scope.model.compositeContentTypes.indexOf(selectedContentType.alias) === -1;

                //based on the selection, we need to filter the available composite types list
                filterAvailableCompositions(selectedContentType, newSelection).then(function () {

                    if (newSelection) {
                        //merge composition with content type

                        //use a different resource lookup depending on the content type type
                        var resourceLookup = scope.contentType === "documentType" ? contentTypeResource.getById : mediaTypeResource.getById;

                        resourceLookup(selectedContentType.id).then(function (composition) {
                            //based on the above filtering we shouldn't be able to select an invalid one, but let's be safe and
                            // double check here.
                            var overlappingAliases = contentTypeHelper.validateAddingComposition(scope.model, composition);
                            if (overlappingAliases.length > 0) {
                                //this will create an invalid composition, need to uncheck it
                                scope.compositionsDialogModel.compositeContentTypes.splice(
                                    scope.compositionsDialogModel.compositeContentTypes.indexOf(composition.alias), 1);
                                //dissallow this until something else is unchecked
                                selectedContentType.disallow = true;
                            }
                            else {
                                contentTypeHelper.mergeCompositeContentType(scope.model, composition);
                            }
                        });
                    }
                    else {
                        // split composition from content type
                        contentTypeHelper.splitCompositeContentType(scope.model, selectedContentType);
                    }

                });

            }
        };

        var availableContentTypeResource = scope.contentType === "documentType" ? contentTypeResource.getAvailableCompositeContentTypes : mediaTypeResource.getAvailableCompositeContentTypes;
        var countContentTypeResource = scope.contentType === "documentType" ? contentTypeResource.getCount : mediaTypeResource.getCount;

          //get the currently assigned property type aliases - ensure we pass these to the server side filer
          var propAliasesExisting = _.filter(_.flatten(_.map(scope.model.groups, function(g) {
              return _.map(g.properties, function(p) {
                  return p.alias;
              });
          })), function(f) {
              return f !== null && f !== undefined;
          });
          $q.all([
              //get available composite types
              availableContentTypeResource(scope.model.id, [], propAliasesExisting).then(function (result) {
                  scope.compositionsDialogModel.availableCompositeContentTypes = result;
                  // convert icons for composite content types
                  iconHelper.formatContentTypeIcons(scope.compositionsDialogModel.availableCompositeContentTypes);
              }),
              //get content type count
              countContentTypeResource().then(function(result) {
                  scope.compositionsDialogModel.totalContentTypes = parseInt(result, 10);
              })
          ]).then(function() {
              //resolves when both other promises are done, now show it
              scope.compositionsDialogModel.show = true;
          });

      };


      /* ---------- GROUPS ---------- */

      scope.addGroup = function(group) {

        // set group sort order
        var index = scope.model.groups.indexOf(group);
        var prevGroup = scope.model.groups[index - 1];

        if( index > 0) {
          // set index to 1 higher than the previous groups sort order
          group.sortOrder = prevGroup.sortOrder + 1;

        } else {
          // first group - sort order will be 0
          group.sortOrder = 0;
        }

        // activate group
        scope.activateGroup(group);

      };

      scope.activateGroup = function(selectedGroup) {

        // set all other groups that are inactive to active
        angular.forEach(scope.model.groups, function(group) {
          // skip init tab
          if (group.tabState !== "init") {
            group.tabState = "inActive";
          }
        });

        selectedGroup.tabState = "active";

      };

      scope.removeGroup = function(groupIndex) {
        scope.model.groups.splice(groupIndex, 1);
      };

      scope.updateGroupTitle = function(group) {
        if (group.properties.length === 0) {
          addInitProperty(group);
        }
      };

      scope.changeSortOrderValue = function() {
        scope.model.groups = $filter('orderBy')(scope.model.groups, 'sortOrder');
      };

      function addInitGroup(groups) {

        // check i init tab already exists
        var addGroup = true;

        angular.forEach(groups, function(group) {
          if (group.tabState === "init") {
            addGroup = false;
          }
        });

        if (addGroup) {
          groups.push({
            properties: [],
            parentTabContentTypes: [],
            parentTabContentTypeNames: [],
            name: "",
            tabState: "init"
          });
        }

        return groups;
      }

      /* ---------- PROPERTIES ---------- */

      scope.addProperty = function(property, group) {

        // set property sort order
        var index = group.properties.indexOf(property);
        var prevProperty = group.properties[index - 1];

        if( index > 0) {
          // set index to 1 higher than the previous property sort order
          property.sortOrder = prevProperty.sortOrder + 1;

        } else {
          // first property - sort order will be 0
          property.sortOrder = 0;
        }

        // open property settings dialog
        scope.editPropertyTypeSettings(property, group);

      };

      scope.editPropertyTypeSettings = function(property, group) {

        if (!property.inherited) {

          scope.propertySettingsDialogModel = {};
          scope.propertySettingsDialogModel.title = "Property settings";
          scope.propertySettingsDialogModel.property = property;
          scope.propertySettingsDialogModel.contentTypeName = scope.model.name;
          scope.propertySettingsDialogModel.view = "views/common/overlays/contenttypeeditor/propertysettings/propertysettings.html";
          scope.propertySettingsDialogModel.show = true;

          // set state to active to access the preview
          property.propertyState = "active";

          // set property states
          property.dialogIsOpen = true;

          scope.propertySettingsDialogModel.submit = function(model) {

            property.inherited = false;
            property.dialogIsOpen = false;

            // update existing data types
            if(model.updateSameDataTypes) {
              updateSameDataTypes(property);
            }

            // remove dialog
            scope.propertySettingsDialogModel.show = false;
            scope.propertySettingsDialogModel = null;

            // push new init property to group
            addInitProperty(group);

            // set focus on init property
            var numberOfProperties = group.properties.length;
            group.properties[numberOfProperties - 1].focus = true;

            // push new init tab to the scope
            addInitGroup(scope.model.groups);

          };

          scope.propertySettingsDialogModel.close = function(oldModel) {

            // reset all property changes
            property.label = oldModel.property.label;
            property.alias = oldModel.property.alias;
            property.description = oldModel.property.description;
            property.config = oldModel.property.config;
            property.editor = oldModel.property.editor;
            property.view = oldModel.property.view;
            property.dataTypeId = oldModel.property.dataTypeId;
            property.dataTypeIcon = oldModel.property.dataTypeIcon;
            property.dataTypeName = oldModel.property.dataTypeName;

            // because we set state to active, to show a preview, we have to check if has been filled out
            // label is required so if it is not filled we know it is a placeholder
            if(oldModel.property.editor === undefined || oldModel.property.editor === null || oldModel.property.editor === "") {
              property.propertyState = "init";
            } else {
              property.propertyState = oldModel.property.propertyState;
            }

            // remove dialog
            scope.propertySettingsDialogModel.show = false;
            scope.propertySettingsDialogModel = null;

          };

        }
      };

      scope.deleteProperty = function(tab, propertyIndex) {

        // remove property
        tab.properties.splice(propertyIndex, 1);

        // if the last property in group is an placeholder - remove add new tab placeholder
        if(tab.properties.length === 1 && tab.properties[0].propertyState === "init") {

          angular.forEach(scope.model.groups, function(group, index, groups){
            if(group.tabState === 'init') {
              groups.splice(index, 1);
            }
          });

        }

      };

      function addInitProperty(group) {

        var addInitPropertyBool = true;
        var initProperty = {
          label: null,
          alias: null,
          propertyState: "init",
          validation: {
            mandatory: false,
            pattern: null
          }
        };

        // check if there already is an init property
        angular.forEach(group.properties, function(property) {
          if (property.propertyState === "init") {
            addInitPropertyBool = false;
          }
        });

        if (addInitPropertyBool) {
          group.properties.push(initProperty);
        }

        return group;
      }

      function updateSameDataTypes(newProperty) {

        // find each property
        angular.forEach(scope.model.groups, function(group){
          angular.forEach(group.properties, function(property){

            if(property.dataTypeId === newProperty.dataTypeId) {

              // update property data
              property.config = newProperty.config;
              property.editor = newProperty.editor;
              property.view = newProperty.view;
              property.dataTypeId = newProperty.dataTypeId;
              property.dataTypeIcon = newProperty.dataTypeIcon;
              property.dataTypeName = newProperty.dataTypeName;

            }

          });
        });
      }


      var unbindModelWatcher = scope.$watch('model', function(newValue, oldValue) {
        if (newValue !== undefined && newValue.groups !== undefined) {
          activate();
        }
      });

      // clean up
      scope.$on('$destroy', function(){
        unbindModelWatcher();
      });

    }

    var directive = {
      restrict: "E",
      replace: true,
      templateUrl: "views/components/umb-groups-builder.html",
      scope: {
        model: "=",
        compositions: "=",
        sorting: "=",
        contentType: "@"
      },
      link: link
    };

    return directive;
  }

  angular.module('umbraco.directives').directive('umbGroupsBuilder', GroupsBuilderDirective);

})();
